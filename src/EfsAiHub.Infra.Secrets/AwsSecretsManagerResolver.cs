using System.Diagnostics;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Secrets;

public sealed class AwsSecretsManagerResolver : ISecretResolver
{
    private readonly IAmazonSecretsManager _client;
    private readonly ISecretCacheService _cache;
    private readonly ILogger<AwsSecretsManagerResolver> _logger;

    public AwsSecretsManagerResolver(
        IAmazonSecretsManager client,
        ISecretCacheService cache,
        ILogger<AwsSecretsManagerResolver> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(
        string? referenceOrLiteral,
        SecretContext context,
        CancellationToken ct = default)
    {
        var reference = SecretReference.Parse(referenceOrLiteral);
        var scopeTag = context.Scope.ToString().ToLowerInvariant();

        switch (reference)
        {
            case EmptySecretReference:
                return null;

            case LiteralSecretReference literal:
                MetricsRegistry.SecretsLiteralDetected.Add(1,
                    new KeyValuePair<string, object?>("scope", scopeTag));
                _logger.LogWarning(
                    "[SecretResolver] Literal credential reached resolver. scope={Scope} projectId={ProjectId} provider={Provider}.",
                    context.Scope, context.ProjectId, context.Provider);
                return literal.Value;

            case AwsSecretReference aws:
                return await ResolveAwsAsync(aws.Identifier, scopeTag, ct);

            default:
                return null;
        }
    }

    private async Task<string?> ResolveAwsAsync(string identifier, string scopeTag, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (value, layer) = await _cache.GetOrFetchAsync(
                identifier,
                async innerCt =>
                {
                    var response = await _client.GetSecretValueAsync(
                        new GetSecretValueRequest { SecretId = identifier },
                        innerCt);
                    return response.SecretString;
                },
                ct);

            stopwatch.Stop();

            var layerTag = layer.ToString().ToLowerInvariant();
            var resultTag = value is null ? "miss" : "hit";

            MetricsRegistry.SecretsResolutionsTotal.Add(1,
                new KeyValuePair<string, object?>("scope", scopeTag),
                new KeyValuePair<string, object?>("cache_layer", layerTag),
                new KeyValuePair<string, object?>("result", resultTag));
            MetricsRegistry.SecretsResolutionLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("cache_layer", layerTag));

            return value;
        }
        catch (ResourceNotFoundException ex)
        {
            stopwatch.Stop();
            MetricsRegistry.SecretsResolutionsTotal.Add(1,
                new KeyValuePair<string, object?>("scope", scopeTag),
                new KeyValuePair<string, object?>("cache_layer", "aws"),
                new KeyValuePair<string, object?>("result", "error"));
            _logger.LogWarning(ex, "[SecretResolver] Secret '{Identifier}' não encontrado no AWS.", identifier);
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            MetricsRegistry.SecretsResolutionsTotal.Add(1,
                new KeyValuePair<string, object?>("scope", scopeTag),
                new KeyValuePair<string, object?>("cache_layer", "aws"),
                new KeyValuePair<string, object?>("result", "error"));
            _logger.LogError(ex, "[SecretResolver] Falha ao resolver '{Identifier}'.", identifier);
            throw;
        }
    }
}
