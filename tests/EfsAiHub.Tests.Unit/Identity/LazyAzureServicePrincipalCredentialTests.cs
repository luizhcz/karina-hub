using Azure.Core;
using EfsAiHub.Host.Api.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Identity;

[Trait("Category", "Unit")]
public class LazyAzureServicePrincipalCredentialTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?>? values = null)
    {
        var manager = new ConfigurationManager();
        if (values is not null) manager.AddInMemoryCollection(values);
        return manager;
    }

    [Fact]
    public void Constructor_NaoLanca_AindaQueSpAusente()
    {
        var act = () => new LazyAzureServicePrincipalCredential(
            BuildConfig(),
            NullLogger<LazyAzureServicePrincipalCredential>.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetToken_SemSp_LancaInvalidOperation_ApontandoConfigKeysFaltantes()
    {
        var credential = new LazyAzureServicePrincipalCredential(
            BuildConfig(),
            NullLogger<LazyAzureServicePrincipalCredential>.Instance);

        var act = () => credential.GetToken(new TokenRequestContext(["scope/.default"]), default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Azure:ServicePrincipal:TenantId*")
            .WithMessage("*Azure:ServicePrincipal:ClientId*")
            .WithMessage("*Azure:ServicePrincipal:ClientSecret*")
            .WithMessage("*AWS Secrets Manager*");
    }

    [Fact]
    public void GetToken_SpParcialmentePopulado_ApontaApenasFaltantes()
    {
        var credential = new LazyAzureServicePrincipalCredential(
            BuildConfig(new Dictionary<string, string?>
            {
                ["Azure:ServicePrincipal:TenantId"] = "00000000-0000-0000-0000-000000000000",
            }),
            NullLogger<LazyAzureServicePrincipalCredential>.Instance);

        var act = () => credential.GetToken(new TokenRequestContext(["scope/.default"]), default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Azure:ServicePrincipal:ClientId*")
            .WithMessage("*Azure:ServicePrincipal:ClientSecret*");
    }

    [Fact]
    public void GetToken_SpCompleto_NaoLancaInvalidOperation()
    {
        // ClientSecretCredential é instanciado com sucesso — só vai lançar
        // exception de auth quando o SDK tentar trocar o secret por token,
        // o que não acontece num teste unit (não há network call).
        var credential = new LazyAzureServicePrincipalCredential(
            BuildConfig(new Dictionary<string, string?>
            {
                ["Azure:ServicePrincipal:TenantId"]     = "00000000-0000-0000-0000-000000000000",
                ["Azure:ServicePrincipal:ClientId"]     = "00000000-0000-0000-0000-000000000000",
                ["Azure:ServicePrincipal:ClientSecret"] = "fake-secret",
            }),
            NullLogger<LazyAzureServicePrincipalCredential>.Instance);

        // Apenas valida que o factory passa do guard sem lançar InvalidOperation.
        // GetToken em si tentaria network call — fora de escopo do unit test.
        Action probe = () =>
        {
            try { credential.GetToken(new TokenRequestContext(["scope/.default"]), default); }
            catch (InvalidOperationException) { throw; }
            catch { /* qualquer outra exception (network/auth) é OK */ }
        };

        probe.Should().NotThrow<InvalidOperationException>();
    }
}
