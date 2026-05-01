using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Core.Abstractions.Projects;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Observa o consumo diário de tokens/custo por projeto contra os tetos
/// (<c>MaxTokensPerDay</c> / <c>MaxCostUsdPerDay</c>) configurados em
/// <c>ProjectSettings</c>. <b>Não bloqueia.</b> Quando o teto é cruzado,
/// emite um <see cref="ILogger.LogCritical"/> + métrica
/// <c>llm.budget.exceeded{scope=project}</c> e segue.
/// </summary>
/// <remarks>
/// Mudança de política (warning-only): antes este guard retornava <c>(false, reason)</c>
/// e o <c>ProjectRateLimitMiddleware</c> respondia 402 Payment Required. Agora retorna
/// sempre <c>true</c>; a observação fica como sinal pra ops sem travar a aplicação.
/// O <see cref="IncrementAsync"/> continua sendo chamado pelo
/// <c>TokenUsagePersistenceService</c> pra alimentar os contadores Redis (também
/// usados pela UI de uso/billing).
/// </remarks>
public sealed class ProjectBudgetGuard
{
    private readonly IEfsRedisCache _cache;
    private readonly IProjectRepository _projectRepo;
    private readonly ILogger<ProjectBudgetGuard> _logger;

    public ProjectBudgetGuard(
        IEfsRedisCache cache,
        IProjectRepository projectRepo,
        ILogger<ProjectBudgetGuard> logger)
    {
        _cache = cache;
        _projectRepo = projectRepo;
        _logger = logger;
    }

    /// <summary>
    /// Observa o consumo diário do projeto. Sempre retorna <c>(true, null)</c>;
    /// emite log critical + métrica quando algum teto está cruzado.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> CheckAsync(
        string projectId, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project?.Settings is null)
            return (true, null);

        var settings = project.Settings;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        if (settings.MaxTokensPerDay is > 0)
        {
            var tokensKey = $"budget:tokens:{projectId}:{today}";
            var tokensStr = await _cache.GetStringAsync(tokensKey);
            if (long.TryParse(tokensStr, out var tokens) && tokens >= settings.MaxTokensPerDay.Value)
            {
                MetricsRegistry.BudgetExceededWarnings.Add(1,
                    new KeyValuePair<string, object?>("scope", "project"),
                    new KeyValuePair<string, object?>("cause", "tokens"),
                    new KeyValuePair<string, object?>("project_id", projectId));
                _logger.LogCritical(
                    "[Budget] Project daily token cap excedido — request continua. ProjectId={ProjectId} Tokens={Tokens} MaxTokensPerDay={MaxTokensPerDay}",
                    projectId, tokens, settings.MaxTokensPerDay.Value);
            }
        }

        if (settings.MaxCostUsdPerDay is > 0)
        {
            var costKey = $"budget:cost:{projectId}:{today}";
            var costStr = await _cache.GetStringAsync(costKey);
            if (decimal.TryParse(costStr, out var cost) && cost >= settings.MaxCostUsdPerDay.Value)
            {
                MetricsRegistry.BudgetExceededWarnings.Add(1,
                    new KeyValuePair<string, object?>("scope", "project"),
                    new KeyValuePair<string, object?>("cause", "cost"),
                    new KeyValuePair<string, object?>("project_id", projectId));
                _logger.LogCritical(
                    "[Budget] Project daily cost cap excedido — request continua. ProjectId={ProjectId} CostUsd={CostUsd:F4} MaxCostUsdPerDay={MaxCostUsdPerDay:F2}",
                    projectId, cost, settings.MaxCostUsdPerDay.Value);
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Incrementa os contadores diários de tokens e custo para um projeto.
    /// Chamado pelo TokenUsagePersistenceService após persistir um registro.
    /// Chaves expiram em 48h para auto-limpeza.
    /// </summary>
    public async Task IncrementAsync(string projectId, long tokens, decimal costUsd)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var ttl = TimeSpan.FromHours(48);

        if (tokens > 0)
        {
            var tokensKey = _cache.BuildKey($"budget:tokens:{projectId}:{today}");
            await _cache.Database.StringIncrementAsync(tokensKey, tokens);
            await _cache.Database.KeyExpireAsync(tokensKey, ttl);
        }

        if (costUsd > 0)
        {
            // Redis INCRBYFLOAT for decimal cost accumulation
            var costKey = _cache.BuildKey($"budget:cost:{projectId}:{today}");
            await _cache.Database.StringIncrementAsync(costKey, (double)costUsd);
            await _cache.Database.KeyExpireAsync(costKey, ttl);
        }
    }
}
