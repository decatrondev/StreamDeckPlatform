using Deck.Core.Auth;

namespace Deck.Core.Tests;

public class DecatronAuthServiceTests
{
    [Fact]
    public void ComputeCodeChallenge_KnownVerifier_MatchesRfc7636Example()
    {
        // Verifier de ejemplo de la RFC 7636 (Apéndice B) — challenge esperado
        // tomado de la misma fuente, para no depender de recalcular a mano.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = DecatronAuthService.ComputeCodeChallenge(verifier);

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesUrlSafeString_WithoutPadding()
    {
        var verifier = DecatronAuthService.GenerateCodeVerifier();

        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_TwoCalls_ProduceDifferentValues()
    {
        var first = DecatronAuthService.GenerateCodeVerifier();
        var second = DecatronAuthService.GenerateCodeVerifier();

        Assert.NotEqual(first, second);
    }
}
