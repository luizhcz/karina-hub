using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace EfsAiHub.Host.Api.Identity;

/// <summary>
/// TokenCredential que resolve <see cref="ClientSecretCredential"/> sob demanda
/// a partir de <c>Azure:ServicePrincipal:*</c> (populado via Secrets:Bootstrap →
/// AWS Secrets Manager). Não falha no startup — só quando algum SDK Azure de fato
/// pede um token. Loga erro contextual + lança <see cref="InvalidOperationException"/>
/// apontando o que falta no AWS Secrets Manager.
/// </summary>
public sealed class LazyAzureServicePrincipalCredential : TokenCredential
{
    private readonly IConfiguration _config;
    private readonly ILogger<LazyAzureServicePrincipalCredential> _logger;
    private readonly object _gate = new();
    private TokenCredential? _resolved;

    public LazyAzureServicePrincipalCredential(
        IConfiguration config,
        ILogger<LazyAzureServicePrincipalCredential> logger)
    {
        _config = config;
        _logger = logger;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => Resolve().GetToken(requestContext, cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => Resolve().GetTokenAsync(requestContext, cancellationToken);

    private TokenCredential Resolve()
    {
        if (_resolved is not null) return _resolved;

        lock (_gate)
        {
            if (_resolved is not null) return _resolved;

            var tenantId     = _config["Azure:ServicePrincipal:TenantId"];
            var clientId     = _config["Azure:ServicePrincipal:ClientId"];
            var clientSecret = _config["Azure:ServicePrincipal:ClientSecret"];

            var missing = new List<string>(3);
            if (string.IsNullOrWhiteSpace(tenantId))     missing.Add("Azure:ServicePrincipal:TenantId");
            if (string.IsNullOrWhiteSpace(clientId))     missing.Add("Azure:ServicePrincipal:ClientId");
            if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("Azure:ServicePrincipal:ClientSecret");

            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Azure authentication requested but Service Principal is not configured. " +
                    "Missing config keys: {MissingKeys}. Cadastre no AWS Secrets Manager e popule " +
                    "Secrets:Bootstrap apontando refs `secret://aws/...` para essas chaves.",
                    string.Join(", ", missing));

                throw new InvalidOperationException(
                    $"Azure Service Principal not configured. Missing: {string.Join(", ", missing)}. " +
                    "Required to authenticate against Azure Foundry / Content Safety / Azure OpenAI " +
                    "(managed identity) / Document Intelligence. Cadastre o SP no AWS Secrets Manager " +
                    "e popule Secrets:Bootstrap com referências pras 3 chaves.");
            }

            _resolved = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return _resolved;
        }
    }
}
