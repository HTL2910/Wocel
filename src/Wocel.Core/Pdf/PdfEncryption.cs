using System.Security.Cryptography;
using System.Text;

namespace Wocel.Core.Pdf;

[Flags]
public enum PdfPermissions
{
    None = 0,
    Print = 1 << 2,
    ModifyContents = 1 << 3,
    CopyContent = 1 << 4,
    ModifyAnnotations = 1 << 5,
    FillForms = 1 << 8,
    ExtractForAccessibility = 1 << 9,
    AssembleDocument = 1 << 10,
    PrintHighQuality = 1 << 11,
    All = Print | ModifyContents | CopyContent | ModifyAnnotations |
          FillForms | ExtractForAccessibility | AssembleDocument | PrintHighQuality
}

public enum PdfEncryptionAlgorithm
{
    Rc4128,
    Aes128,
    Aes256
}

/// <summary>Thiết lập khi đặt mật khẩu cho file PDF.</summary>
public sealed class PdfEncryptionSettings
{
    /// <summary>Mật khẩu mở file. Để trống nghĩa là ai cũng mở được nhưng vẫn bị giới hạn quyền.</summary>
    public string UserPassword { get; set; } = string.Empty;

    /// <summary>Mật khẩu chủ sở hữu (đổi quyền). Bỏ trống sẽ dùng luôn mật khẩu người dùng.</summary>
    public string OwnerPassword { get; set; } = string.Empty;

    public PdfPermissions Permissions { get; set; } = PdfPermissions.All;
    public PdfEncryptionAlgorithm Algorithm { get; set; } = PdfEncryptionAlgorithm.Aes128;
}

/// <summary>
/// Trình xử lý bảo mật tiêu chuẩn của PDF (ISO 32000-1 §7.6): RC4 40/128-bit,
/// AES-128 (AESV2) và AES-256 (AESV3/R6). Chỉ giải mã khi người dùng cung cấp mật khẩu.
/// </summary>
public sealed class PdfEncryption
{
    private static readonly byte[] Pad =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56,
        0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    };

    private byte[] _fileKey = Array.Empty<byte>();
    private bool _useAes;
    private int _revision = 4;
    private int _version = 4;
    private int _keyLengthBytes = 16;
    private int _permissions = -1;
    private bool _encryptMetadata = true;

    private byte[] _o = Array.Empty<byte>();
    private byte[] _u = Array.Empty<byte>();
    private byte[] _oe = Array.Empty<byte>();
    private byte[] _ue = Array.Empty<byte>();
    private byte[] _perms = Array.Empty<byte>();

    /// <summary>Phiên bản PDF tối thiểu mà thuật toán này yêu cầu.</summary>
    public string? RequiredVersion => _version switch
    {
        >= 5 => "2.0",
        4 => "1.6",
        _ => "1.4"
    };

    public bool OpenedWithOwnerPassword { get; private set; }
    public PdfPermissions Permissions => (PdfPermissions)(_permissions & (int)PdfPermissions.All);

    // ─────────────────────────────────────────────────────────────────────
    //  ĐỌC: dựng khoá từ mật khẩu người dùng cung cấp
    // ─────────────────────────────────────────────────────────────────────
    public static PdfEncryption? TryCreate(
        PdfDictionary encryptDict, byte[] firstId, string password,
        IPdfResolver resolver, out string description)
    {
        var filter = encryptDict.GetName("Filter", resolver);
        int v = encryptDict.GetInt("V", 0, resolver);
        int r = encryptDict.GetInt("R", 2, resolver);
        int lengthBits = encryptDict.GetInt("Length", 40, resolver);

        description = $"Filter={filter ?? "?"} V={v} R={r} Length={lengthBits}";

        if (filter != "Standard")
        {
            description += " (trình bảo mật không phải Standard — không hỗ trợ)";
            return null;
        }

        var handler = new PdfEncryption
        {
            _version = v,
            _revision = r,
            _permissions = encryptDict.GetInt("P", -1, resolver),
            _encryptMetadata = encryptDict.GetBool("EncryptMetadata", true, resolver),
            _o = (encryptDict.Get("O", resolver) as PdfString)?.Bytes ?? Array.Empty<byte>(),
            _u = (encryptDict.Get("U", resolver) as PdfString)?.Bytes ?? Array.Empty<byte>(),
            _oe = (encryptDict.Get("OE", resolver) as PdfString)?.Bytes ?? Array.Empty<byte>(),
            _ue = (encryptDict.Get("UE", resolver) as PdfString)?.Bytes ?? Array.Empty<byte>(),
            _keyLengthBytes = Math.Clamp(lengthBits / 8, 5, 32)
        };

        if (v >= 4)
        {
            var cryptFilters = encryptDict.GetDictionary("CF", resolver);
            var streamFilterName = encryptDict.GetName("StmF", resolver) ?? "Identity";
            var selected = cryptFilters.GetDictionary(streamFilterName, resolver);
            var method = selected.GetName("CFM", resolver) ?? "V2";

            handler._useAes = method is "AESV2" or "AESV3";
            int cfLength = selected.GetInt("Length", 0, resolver);
            if (cfLength > 0) handler._keyLengthBytes = cfLength <= 40 ? cfLength : cfLength / 8;
            if (method == "AESV3") handler._keyLengthBytes = 32;

            description += $" CFM={method}";
        }
        else
        {
            handler._useAes = false;
        }

        bool ok = r >= 5
            ? handler.AuthenticateR5R6(password)
            : handler.AuthenticateLegacy(password, firstId);

        if (!ok)
        {
            description += " — mật khẩu không đúng hoặc chưa nhập";
            return null;
        }

        description += handler.OpenedWithOwnerPassword ? " — mở bằng mật khẩu chủ sở hữu" : " — đã mở khoá";
        return handler;
    }

    private bool AuthenticateLegacy(string password, byte[] firstId)
    {
        var passwordBytes = EncodePassword(password);

        // Thử như mật khẩu người dùng.
        var key = ComputeLegacyKey(passwordBytes, firstId);
        if (CheckUserPassword(key, passwordBytes, firstId))
        {
            _fileKey = key;
            return true;
        }

        // Thử như mật khẩu chủ sở hữu: giải O ra mật khẩu người dùng.
        var ownerKey = ComputeOwnerKey(passwordBytes);
        var userPassword = _revision == 2
            ? Rc4.Transform(ownerKey, _o)
            : DecryptOwnerIterative(ownerKey, _o);

        key = ComputeLegacyKey(userPassword, firstId);
        if (CheckUserPassword(key, userPassword, firstId))
        {
            _fileKey = key;
            OpenedWithOwnerPassword = true;
            return true;
        }

        return false;
    }

    private byte[] DecryptOwnerIterative(byte[] ownerKey, byte[] data)
    {
        var current = data;
        for (int i = 19; i >= 0; i--)
        {
            var round = new byte[ownerKey.Length];
            for (int j = 0; j < ownerKey.Length; j++) round[j] = (byte)(ownerKey[j] ^ i);
            current = Rc4.Transform(round, current);
        }
        return current;
    }

    private byte[] ComputeLegacyKey(byte[] paddedPassword, byte[] firstId)
    {
        using var md5 = MD5.Create();
        var buffer = new MemoryStream();

        var padded = PadPassword(paddedPassword);
        buffer.Write(padded, 0, padded.Length);
        buffer.Write(_o, 0, Math.Min(_o.Length, 32));

        buffer.WriteByte((byte)_permissions);
        buffer.WriteByte((byte)(_permissions >> 8));
        buffer.WriteByte((byte)(_permissions >> 16));
        buffer.WriteByte((byte)(_permissions >> 24));

        buffer.Write(firstId, 0, firstId.Length);

        if (_revision >= 4 && !_encryptMetadata)
            buffer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        var hash = md5.ComputeHash(buffer.ToArray());
        int keyLength = _revision == 2 ? 5 : _keyLengthBytes;

        if (_revision >= 3)
        {
            for (int i = 0; i < 50; i++)
                hash = MD5.HashData(hash.Take(keyLength).ToArray());
        }

        return hash.Take(keyLength).ToArray();
    }

    private byte[] ComputeOwnerKey(byte[] password)
    {
        var hash = MD5.HashData(PadPassword(password));
        int keyLength = _revision == 2 ? 5 : _keyLengthBytes;

        if (_revision >= 3)
            for (int i = 0; i < 50; i++) hash = MD5.HashData(hash);

        return hash.Take(keyLength).ToArray();
    }

    private bool CheckUserPassword(byte[] key, byte[] password, byte[] firstId)
    {
        if (_u.Length == 0) return false;

        if (_revision == 2)
            return Rc4.Transform(key, PadPassword(password)).Take(32).SequenceEqual(_u.Take(32));

        var hash = MD5.HashData(PadPassword(Array.Empty<byte>()).Concat(firstId).ToArray());
        var encrypted = Rc4.Transform(key, hash);

        for (int i = 1; i <= 19; i++)
        {
            var round = new byte[key.Length];
            for (int j = 0; j < key.Length; j++) round[j] = (byte)(key[j] ^ i);
            encrypted = Rc4.Transform(round, encrypted);
        }

        return encrypted.Take(16).SequenceEqual(_u.Take(16));
    }

    private bool AuthenticateR5R6(string password)
    {
        if (_u.Length < 48) return false;
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var validationSalt = _u.Skip(32).Take(8).ToArray();
        var keySalt = _u.Skip(40).Take(8).ToArray();

        if (Hash2B(passwordBytes, validationSalt, Array.Empty<byte>()).SequenceEqual(_u.Take(32)))
        {
            var intermediate = Hash2B(passwordBytes, keySalt, Array.Empty<byte>());
            _fileKey = AesNoPadding(intermediate, new byte[16], _ue, encrypt: false);
            _useAes = true;
            _keyLengthBytes = 32;
            return _fileKey.Length == 32;
        }

        if (_o.Length >= 48)
        {
            var u48 = _u.Take(48).ToArray();
            var ownerValidation = _o.Skip(32).Take(8).ToArray();
            var ownerKeySalt = _o.Skip(40).Take(8).ToArray();

            if (Hash2B(passwordBytes, ownerValidation, u48).SequenceEqual(_o.Take(32)))
            {
                var intermediate = Hash2B(passwordBytes, ownerKeySalt, u48);
                _fileKey = AesNoPadding(intermediate, new byte[16], _oe, encrypt: false);
                _useAes = true;
                _keyLengthBytes = 32;
                OpenedWithOwnerPassword = true;
                return _fileKey.Length == 32;
            }
        }

        return false;
    }

    /// <summary>Thuật toán 2.B (R6) — băm tăng cường bằng vòng lặp AES-128-CBC.</summary>
    private byte[] Hash2B(byte[] password, byte[] salt, byte[] userData)
    {
        var input = password.Concat(salt).Concat(userData).ToArray();
        var k = SHA256.HashData(input);
        if (_revision == 5) return k;

        byte[] e = Array.Empty<byte>();

        for (int round = 0; round < 64 || e[^1] > round - 32; round++)
        {
            var block = password.Concat(k).Concat(userData).ToArray();
            var k1 = new byte[block.Length * 64];
            for (int i = 0; i < 64; i++) Buffer.BlockCopy(block, 0, k1, i * block.Length, block.Length);

            e = AesNoPadding(k.Take(16).ToArray(), k.Skip(16).Take(16).ToArray(), k1, encrypt: true);

            int sum = 0;
            for (int i = 0; i < 16 && i < e.Length; i++) sum += e[i];

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e)
            };
        }

        return k.Take(32).ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GHI: tạo khoá mới từ thiết lập người dùng
    // ─────────────────────────────────────────────────────────────────────
    public static PdfEncryption CreateForWriting(PdfEncryptionSettings settings, byte[] fileId)
    {
        var handler = new PdfEncryption
        {
            _permissions = unchecked((int)0xFFFFF0C0) | (int)settings.Permissions,
            _encryptMetadata = true
        };

        string userPassword = settings.UserPassword;
        string ownerPassword = string.IsNullOrEmpty(settings.OwnerPassword) ? userPassword : settings.OwnerPassword;

        switch (settings.Algorithm)
        {
            case PdfEncryptionAlgorithm.Aes256:
                handler._version = 5;
                handler._revision = 6;
                handler._keyLengthBytes = 32;
                handler._useAes = true;
                handler.SetupAes256(userPassword, ownerPassword);
                break;

            case PdfEncryptionAlgorithm.Aes128:
                handler._version = 4;
                handler._revision = 4;
                handler._keyLengthBytes = 16;
                handler._useAes = true;
                handler.SetupLegacy(userPassword, ownerPassword, fileId);
                break;

            default:
                handler._version = 2;
                handler._revision = 3;
                handler._keyLengthBytes = 16;
                handler._useAes = false;
                handler.SetupLegacy(userPassword, ownerPassword, fileId);
                break;
        }

        return handler;
    }

    private void SetupLegacy(string userPassword, string ownerPassword, byte[] fileId)
    {
        var userBytes = EncodePassword(userPassword);
        var ownerBytes = EncodePassword(ownerPassword);

        // Thuật toán 3: tính /O
        var ownerKey = ComputeOwnerKey(ownerBytes);
        var o = Rc4.Transform(ownerKey, PadPassword(userBytes));
        if (_revision >= 3)
        {
            for (int i = 1; i <= 19; i++)
            {
                var round = new byte[ownerKey.Length];
                for (int j = 0; j < ownerKey.Length; j++) round[j] = (byte)(ownerKey[j] ^ i);
                o = Rc4.Transform(round, o);
            }
        }
        _o = o;

        // Thuật toán 2: khoá file
        _fileKey = ComputeLegacyKey(userBytes, fileId);

        // Thuật toán 4/5: tính /U
        if (_revision == 2)
        {
            _u = Rc4.Transform(_fileKey, Pad);
        }
        else
        {
            var hash = MD5.HashData(Pad.Concat(fileId).ToArray());
            var u = Rc4.Transform(_fileKey, hash);
            for (int i = 1; i <= 19; i++)
            {
                var round = new byte[_fileKey.Length];
                for (int j = 0; j < _fileKey.Length; j++) round[j] = (byte)(_fileKey[j] ^ i);
                u = Rc4.Transform(round, u);
            }
            _u = u.Concat(new byte[16]).Take(32).ToArray();
        }
    }

    private void SetupAes256(string userPassword, string ownerPassword)
    {
        _fileKey = RandomNumberGenerator.GetBytes(32);
        var userBytes = Encoding.UTF8.GetBytes(userPassword);
        var ownerBytes = Encoding.UTF8.GetBytes(ownerPassword);

        // Thuật toán 8: /U và /UE
        var userValidationSalt = RandomNumberGenerator.GetBytes(8);
        var userKeySalt = RandomNumberGenerator.GetBytes(8);
        _u = Hash2B(userBytes, userValidationSalt, Array.Empty<byte>())
            .Concat(userValidationSalt).Concat(userKeySalt).ToArray();
        _ue = AesNoPadding(Hash2B(userBytes, userKeySalt, Array.Empty<byte>()), new byte[16], _fileKey, encrypt: true);

        // Thuật toán 9: /O và /OE (dùng /U 48 byte làm dữ liệu bổ sung)
        var ownerValidationSalt = RandomNumberGenerator.GetBytes(8);
        var ownerKeySalt = RandomNumberGenerator.GetBytes(8);
        _o = Hash2B(ownerBytes, ownerValidationSalt, _u)
            .Concat(ownerValidationSalt).Concat(ownerKeySalt).ToArray();
        _oe = AesNoPadding(Hash2B(ownerBytes, ownerKeySalt, _u), new byte[16], _fileKey, encrypt: true);

        // Thuật toán 10: /Perms
        var perms = new byte[16];
        perms[0] = (byte)_permissions;
        perms[1] = (byte)(_permissions >> 8);
        perms[2] = (byte)(_permissions >> 16);
        perms[3] = (byte)(_permissions >> 24);
        perms[4] = 0xFF; perms[5] = 0xFF; perms[6] = 0xFF; perms[7] = 0xFF;
        perms[8] = (byte)(_encryptMetadata ? 'T' : 'F');
        perms[9] = (byte)'a'; perms[10] = (byte)'d'; perms[11] = (byte)'b';
        RandomNumberGenerator.Fill(perms.AsSpan(12, 4));

        using var aes = Aes.Create();
        aes.Key = _fileKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        _perms = aes.EncryptEcb(perms, PaddingMode.None);
    }

    public PdfDictionary BuildEncryptDictionary()
    {
        var dict = new PdfDictionary();
        dict.SetName("Filter", "Standard");
        dict.SetInt("V", _version);
        dict.SetInt("R", _revision);
        dict.SetInt("P", _permissions);
        dict["O"] = new PdfString(_o, preferHex: true);
        dict["U"] = new PdfString(_u, preferHex: true);

        if (_version >= 5)
        {
            dict.SetInt("Length", 256);
            dict["OE"] = new PdfString(_oe, preferHex: true);
            dict["UE"] = new PdfString(_ue, preferHex: true);
            dict["Perms"] = new PdfString(_perms, preferHex: true);

            var cf = new PdfDictionary();
            var stdCf = new PdfDictionary();
            stdCf.SetName("CFM", "AESV3");
            stdCf.SetName("AuthEvent", "DocOpen");
            stdCf.SetInt("Length", 32);
            cf["StdCF"] = stdCf;
            dict["CF"] = cf;
            dict.SetName("StmF", "StdCF");
            dict.SetName("StrF", "StdCF");
        }
        else if (_version == 4)
        {
            dict.SetInt("Length", _keyLengthBytes * 8);

            var cf = new PdfDictionary();
            var stdCf = new PdfDictionary();
            stdCf.SetName("CFM", _useAes ? "AESV2" : "V2");
            stdCf.SetName("AuthEvent", "DocOpen");
            stdCf.SetInt("Length", _keyLengthBytes);
            cf["StdCF"] = stdCf;
            dict["CF"] = cf;
            dict.SetName("StmF", "StdCF");
            dict.SetName("StrF", "StdCF");
        }
        else
        {
            dict.SetInt("Length", _keyLengthBytes * 8);
        }

        return dict;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Mã hoá / giải mã dữ liệu từng đối tượng
    // ─────────────────────────────────────────────────────────────────────
    public byte[] Decrypt(byte[] data, int objectNumber, int generation, bool isString)
        => Transform(data, objectNumber, generation, encrypt: false);

    public byte[] Encrypt(byte[] data, int objectNumber, int generation, bool isString)
        => Transform(data, objectNumber, generation, encrypt: true);

    private byte[] Transform(byte[] data, int objectNumber, int generation, bool encrypt)
    {
        if (data.Length == 0 || _fileKey.Length == 0) return data;

        var key = _version >= 5 ? _fileKey : ComputeObjectKey(objectNumber, generation);

        if (!_useAes) return Rc4.Transform(key, data);

        if (encrypt)
        {
            var iv = RandomNumberGenerator.GetBytes(16);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            var body = aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
            return iv.Concat(body).ToArray();
        }

        if (data.Length <= 16) return Array.Empty<byte>();

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            return aes.DecryptCbc(data.Skip(16).ToArray(), data.Take(16).ToArray(), PaddingMode.PKCS7);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private byte[] ComputeObjectKey(int objectNumber, int generation)
    {
        var buffer = new List<byte>(_fileKey);
        buffer.Add((byte)objectNumber);
        buffer.Add((byte)(objectNumber >> 8));
        buffer.Add((byte)(objectNumber >> 16));
        buffer.Add((byte)generation);
        buffer.Add((byte)(generation >> 8));

        if (_useAes) buffer.AddRange(new byte[] { 0x73, 0x41, 0x6C, 0x54 }); // "sAlT"

        var hash = MD5.HashData(buffer.ToArray());
        int length = Math.Min(_fileKey.Length + 5, 16);
        return hash.Take(length).ToArray();
    }

    private static byte[] AesNoPadding(byte[] key, byte[] iv, byte[] data, bool encrypt)
    {
        if (data.Length == 0 || data.Length % 16 != 0) return Array.Empty<byte>();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        return encrypt ? aes.EncryptCbc(data, iv, PaddingMode.None) : aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] EncodePassword(string password)
    {
        // PDFDocEncoding cho R<=4 — Latin1 là xấp xỉ đủ dùng.
        return Encoding.Latin1.GetBytes(password);
    }

    private static byte[] PadPassword(byte[] password)
    {
        var result = new byte[32];
        int copy = Math.Min(password.Length, 32);
        Buffer.BlockCopy(password, 0, result, 0, copy);
        Buffer.BlockCopy(Pad, 0, result, copy, 32 - copy);
        return result;
    }
}

/// <summary>RC4 — chỉ dùng để đọc/ghi các file PDF đời cũ theo đúng đặc tả.</summary>
internal static class Rc4
{
    public static byte[] Transform(byte[] key, byte[] data)
    {
        if (key.Length == 0) return data;

        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }
}
