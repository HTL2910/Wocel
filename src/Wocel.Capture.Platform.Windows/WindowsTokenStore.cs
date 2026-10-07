using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform.Windows;

public interface IWindowsDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public sealed class WindowsCredentialException(string errorCode, Exception? innerException = null)
    : Exception("Windows could not access the protected Google credential.", innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class WindowsTokenStore(string path, IWindowsDataProtector? protector = null) : IProtectedTokenStore
{
    private readonly IWindowsDataProtector _protector = protector ?? new DpapiDataProtector();

    public async Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var plaintext = Encoding.UTF8.GetBytes(token);
        byte[] protectedBytes;
        try
        {
            protectedBytes = _protector.Protect(plaintext);
        }
        catch (Exception exception) when (exception is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new WindowsCredentialException("CREDENTIAL_PROTECT_FAILED", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var ciphertext = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(ciphertext);
        }
        catch (Exception exception) when (exception is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new WindowsCredentialException("CREDENTIAL_UNPROTECT_FAILED", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

internal sealed partial class DpapiDataProtector : IWindowsDataProtector
{
    private const uint UiForbidden = 0x1;
    public byte[] Protect(byte[] plaintext) => Transform(plaintext, true);
    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI is available only on Windows.");
        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, input.Data, data.Length);
        try
        {
            var succeeded = protect
                ? CryptProtectData(ref input, null, 0, 0, 0, UiForbidden, out var output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, UiForbidden, out output);
            if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
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
            Marshal.FreeHGlobal(input.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public nint Data; }
    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob input, [MarshalAs(UnmanagedType.LPWStr)] string? description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
