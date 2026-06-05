using System.Diagnostics;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Secrets;

/// <summary>
/// Eager-load de todos os <c>secret://aws/...</c> conhecidos no boot. Coleta
/// referências a partir de <see cref="ISecretReferenceSource"/> registrados no
/// DI (cada source conhece um lugar onde refs aparecem — config, projects,
/// agents, etc.). Deduplica por identifier AWS, resolve em paralelo
/// (Task.WhenAll) com paralelismo limitado, trata "secret não existe" como
/// warning (não derruba o boot) — o caller que referencia recebe null em
/// runtime e produz erro contextual próprio.
/// </summary>
public sealed class SecretsPreloader
{
    private readonly IAmazonSecretsManager _aws;
    private readonly ILogger<SecretsPreloader> _logger;
    private readonly IReadOnlyDictionary<string, string?> _bootstrapReferences;
    private readonly IReadOnlyList<ISecretReferenceSource> _sources;

    public SecretsPreloader(
        IAmazonSecretsManager aws,
        ILogger<SecretsPreloader> logger,
        IReadOnlyDictionary<string, string?> bootstrapReferences,
        IReadOnlyList<ISecretReferenceSource> sources)
    {
        _aws = aws;
        _logger = logger;
        _bootstrapReferences = bootstrapReferences;
        _sources = sources;
    }

    /// <summary>
    /// Executa a varredura + resolução. Retorna o dicionário <c>identifier → value</c>
    /// pronto pra alimentar o <see cref="RuntimeSecretStore"/>. Erros de AWS
    /// individuais viram warnings; falha total (AWS offline) lança e bloqueia o boot.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        // Bootstrap entries (config keys já resolvidos in-memory na ConfigurationManager,
        // mas precisamos das refs originais pra popular o store indexado por identifier).
        foreach (var (_, refValue) in _bootstrapReferences)
            TryAddAwsIdentifier(identifiers, refValue);

        // Sources externos (projects, agents, etc.). Cada source isola sua coleta;
        // falha de um source não propaga.
        foreach (var source in _sources)
        {
            try
            {
                var refs = await source.CollectAsync(ct);
                foreach (var r in refs)
                    TryAddAwsIdentifier(identifiers, r);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SecretsPreloader] Source '{Source}' falhou ao coletar refs — secrets daquele source não serão pré-carregados.",
                    source.Name);
            }
        }

        if (identifiers.Count == 0)
        {
            _logger.LogInformation(
                "[SecretsPreloader] Nenhuma referência AWS encontrada. Store iniciado vazio.");
            MetricsRegistry.SecretsPreloadDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        _logger.LogInformation(
            "[SecretsPreloader] Resolvendo {Count} referência(s) AWS únicas no boot…",
            identifiers.Count);

        var results = new Dictionary<string, string?>(StringComparer.Ordinal);
        var failures = 0;

        // Paralelismo limitado evita rate-limit do AWS. Default 10 in-flight
        // — suficiente pra dezenas de refs sem custo perceptível.
        const int maxParallel = 10;
        using var sem = new SemaphoreSlim(maxParallel);
        var tasks = identifiers.Select(async identifier =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var value = await FetchOneAsync(identifier, ct);
                return (identifier, value, error: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[SecretsPreloader] Falha ao resolver '{Identifier}'. Caller receberá null em runtime.",
                    identifier);
                return (identifier, value: (string?)null, error: true);
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        var resolved = await Task.WhenAll(tasks);
        foreach (var (identifier, value, error) in resolved)
        {
            results[identifier] = value;
            if (error) failures++;
        }

        sw.Stop();
        MetricsRegistry.SecretsPreloadDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        if (failures > 0)
            MetricsRegistry.SecretsPreloadFailures.Add(failures);

        _logger.LogInformation(
            "[SecretsPreloader] Concluído em {DurationMs:F0}ms: {Resolved}/{Total} referência(s) resolvida(s), {Failed} falha(s).",
            sw.Elapsed.TotalMilliseconds, identifiers.Count - failures, identifiers.Count, failures);

        return results;
    }

    private async Task<string?> FetchOneAsync(string identifier, CancellationToken ct)
    {
        try
        {
            var response = await _aws.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = identifier }, ct);
            return response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning(
                "[SecretsPreloader] Secret '{Identifier}' não encontrado no AWS. Removendo da store.",
                identifier);
            return null;
        }
    }

    private static void TryAddAwsIdentifier(HashSet<string> identifiers, string? refValue)
    {
        if (string.IsNullOrWhiteSpace(refValue)) return;
        if (SecretReference.Parse(refValue) is AwsSecretReference aws)
            identifiers.Add(aws.Identifier);
    }
}
