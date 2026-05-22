using System.Collections.Immutable;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Secrets;

/// <summary>
/// Snapshot imutável de segredos AWS resolvidos durante <c>SecretsPreloader</c>.
/// Lookup é dictionary-hit puro (sub-microssegundo); zero chamadas a AWS após o boot.
/// </summary>
public sealed class RuntimeSecretStore : IRuntimeSecretStore
{
    private readonly ImmutableDictionary<string, string?> _values;
    private readonly ILogger<RuntimeSecretStore> _logger;

    public RuntimeSecretStore(
        IReadOnlyDictionary<string, string?> values,
        ILogger<RuntimeSecretStore> logger)
    {
        _values = values.ToImmutableDictionary(StringComparer.Ordinal);
        _logger = logger;
    }

    public int LoadedCount => _values.Count;

    public string? Get(string? referenceOrLiteral)
    {
        var reference = SecretReference.Parse(referenceOrLiteral);
        switch (reference)
        {
            case EmptySecretReference:
                return null;

            case LiteralSecretReference literal:
                // Literal vazamento em config é um sinal de que alguém colou
                // a key crua no settings — deveria estar como secret://aws/.
                // Log uma vez por chave pra não inundar (HashSet daria mais
                // ruído pra manutenção; aceitamos a duplicação no log).
                MetricsRegistry.SecretsLiteralDetected.Add(1);
                _logger.LogWarning(
                    "[RuntimeSecretStore] Literal credential atingiu o store. Cadastre como 'secret://aws/...' no AWS Secrets Manager.");
                return literal.Value;

            case AwsSecretReference aws:
                if (_values.TryGetValue(aws.Identifier, out var v))
                    return v;

                // Secret novo cadastrado depois do boot — política operacional
                // exige restart. Retorna null pra o caller usar a validação
                // existente (a maioria já lança InvalidOperationException
                // com mensagem útil pro operador).
                _logger.LogError(
                    "[RuntimeSecretStore] Secret '{Identifier}' não foi pré-carregado no boot. " +
                    "Adicione a referência a um project/agent + restart do pod.",
                    aws.Identifier);
                return null;

            default:
                return null;
        }
    }
}
