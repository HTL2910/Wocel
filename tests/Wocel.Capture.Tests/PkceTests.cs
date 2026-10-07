using Wocel.Capture.Cloud;
using Xunit;

namespace Wocel.Capture.Tests;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_matches_rfc7636_s256_vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.CreateChallenge(verifier));
    }

    [Fact]
    public void Generated_verifier_has_required_length_and_url_safe_characters()
    {
        var verifier = Pkce.GenerateVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9._~-]+$", verifier);
    }

    [Fact]
    public void OAuth_state_rejects_mismatch_and_expiry()
    {
        var created = DateTimeOffset.Parse("2026-10-05T08:00:00Z");
        var validator = new OAuthStateValidator("expected-state", created, TimeSpan.FromMinutes(5));

        Assert.True(validator.IsValid("expected-state", created.AddMinutes(4)));
        Assert.False(validator.IsValid("wrong-state", created.AddMinutes(1)));
        Assert.False(validator.IsValid("expected-state", created.AddMinutes(6)));
    }
}
