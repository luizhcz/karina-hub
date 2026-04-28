using Azure;
using Azure.Core;
using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.Extensions.AI;
using MeaiSafety = Microsoft.Extensions.AI.Evaluation.Safety;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Resolve <see cref="IChatClient"/> apontando pra deployment Azure AI Foundry dedicado do projeto. Cache por projectId com TTL de 5min.</summary>
public sealed class FoundryJudgeClientFactory : IFoundryJudgeClientFactory
{
    private readonly IProjectRepository _projectRepo;
    private readonly TokenCredential _credential;
    private readonly ILogger<FoundryJudgeClientFactory> _logger;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    private record CacheEntry(FoundryJudgeConfig Config, DateTime ExpiresAt);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _cache = new();

    public FoundryJudgeClientFactory(
        IProjectRepository projectRepo,
        TokenCredential credential,
        ILogger<FoundryJudgeClientFactory> logger)
    {
        _projectRepo = projectRepo;
        _credential = credential;
        _logger = logger;
    }

    public async Task<FoundryJudgeConfig?> ResolveAsync(string projectId, CancellationToken ct)
    {
        if (_cache.TryGetValue(projectId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Config;

        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project is null)
        {
            _logger.LogWarning("[FoundryJudgeClientFactory] Project '{ProjectId}' não encontrado.", projectId);
            return null;
        }

        var foundry = project.Settings.Evaluation?.Foundry;
        if (foundry is null || !foundry.Enabled)
        {
            _logger.LogDebug(
                "[FoundryJudgeClientFactory] Project '{ProjectId}' sem 'evaluation.foundry' habilitado.", projectId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(foundry.Endpoint) || string.IsNullOrWhiteSpace(foundry.ModelDeployment))
        {
            _logger.LogWarning(
                "[FoundryJudgeClientFactory] Project '{ProjectId}' tem 'evaluation.foundry.Enabled=true' mas Endpoint/ModelDeployment vazios.",
                projectId);
            return null;
        }

        // Guard contra "***" — chave mascarada do GET response. Sem isso,
        // AzureKeyCredential("***") gera 401 em todo case × evaluator × repetição.
        if (foundry.ApiKeyRef == "***")
        {
            _logger.LogError(
                "[FoundryJudgeClientFactory] Project '{ProjectId}' tem ApiKeyRef='***' (mascarado). " +
                "Reconfigure a chave via UI — o response do GET mascara chaves literais.", projectId);
            return null;
        }

        var endpoint = new Uri(foundry.Endpoint!);
        Azure.AI.OpenAI.AzureOpenAIClient azureClient;
        if (!string.IsNullOrWhiteSpace(foundry.ApiKeyRef) && !foundry.ApiKeyRef!.StartsWith("secret://", StringComparison.OrdinalIgnoreCase))
        {
            azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, new AzureKeyCredential(foundry.ApiKeyRef!));
        }
        else
        {
            azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, _credential);
        }

        var chatClient = azureClient.GetChatClient(foundry.ModelDeployment!).AsIChatClient();

        // Safety auth obrigatoriamente via TokenCredential — Azure Content Safety NÃO suporta API key.
        MeaiSafety.ContentSafetyServiceConfiguration? safetyConfig = null;
        if (!string.IsNullOrWhiteSpace(foundry.ProjectEndpoint))
        {
            try
            {
                var projectUri = new Uri(foundry.ProjectEndpoint!);
                safetyConfig = new MeaiSafety.ContentSafetyServiceConfiguration(_credential, projectUri);
            }
            catch (UriFormatException ex)
            {
                _logger.LogWarning(ex,
                    "[FoundryJudgeClientFactory] Project '{ProjectId}' tem ProjectEndpoint inválido — Safety bindings serão pulados.",
                    projectId);
            }
        }

        var config = new FoundryJudgeConfig(chatClient, foundry.ModelDeployment!, foundry.Endpoint!, safetyConfig);

        _cache[projectId] = new CacheEntry(config, DateTime.UtcNow + _cacheTtl);
        return config;
    }
}
