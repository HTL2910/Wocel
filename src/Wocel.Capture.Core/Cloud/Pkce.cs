using System.Security.Cryptography;
using System.Text;

namespace Wocel.Capture.Cloud;

public static class Pkce
{
    public static string GenerateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));
    public static string GenerateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateChallenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        if (verifier.Length is < 43 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(verifier), "PKCE verifier must be 43-128 characters.");
        }
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class OAuthStateValidator(string expectedState, DateTimeOffset createdAt, TimeSpan lifetime)
{
    public bool IsValid(string? receivedState, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(receivedState) || now < createdAt || now - createdAt > lifetime)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(expectedState);
        var received = Encoding.UTF8.GetBytes(receivedState);
        return expected.Length == received.Length && CryptographicOperations.FixedTimeEquals(expected, received);
    }
}
