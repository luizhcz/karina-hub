using EfsAiHub.Core.Abstractions.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EfsAiHub.Infra.Secrets.Health;

/// <summary>
/// Health check do runtime secret store. Reporta Healthy quando o store foi
/// inicializado no boot (mesmo vazio — esperado em dev sem secrets).
/// Reporta Unhealthy se o store ainda não foi populado (cenário de erro de boot).
///
/// Não bate mais no AWS Secrets Manager em runtime — todas as referências
/// foram pré-carregadas no boot. Liveness não pode depender de serviço externo
/// pós-startup; AWS é dependência de boot, não de runtime.
/// </summary>
public sealed class AwsSecretsHealthCheck : IHealthCheck
{
    private readonly IRuntimeSecretStore _store;

    public AwsSecretsHealthCheck(IRuntimeSecretStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = _store.LoadedCount;
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Runtime secret store ativo com {loaded} segredo(s) AWS pré-carregado(s)."));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Runtime secret store não foi inicializado — boot incompleto.", ex));
        }
    }
}
