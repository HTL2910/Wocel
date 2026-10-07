using System.Runtime.InteropServices;
using System.Text;

namespace Wocel.Capture.Windows.Security;

public interface IProtectedTokenStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed partial class DpapiTokenStore(string path) : IProtectedTokenStore
{
    private const uint CryptprotectUiForbidden = 0x1;

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(Unprotect(protectedBytes));
    }

    public async Task SaveAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, Protect(Encoding.UTF8.GetBytes(refreshToken)), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] data) => Transform(data, protect: true);
    private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, input.Data, data.Length);
        try
        {
            var success = protect
                ? CryptProtectData(ref input, null, 0, 0, 0, CryptprotectUiForbidden, out var output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, CryptprotectUiForbidden, out output);
            if (!success)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the Google credential.");
            }
            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(data);
            Marshal.FreeHGlobal(input.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob input, [MarshalAs(UnmanagedType.LPWStr)] string? description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
