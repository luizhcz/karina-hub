using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Core.Abstractions.Projects;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Verifica se o projeto excedeu o budget diário (MaxCostUsdPerDay / MaxTokensPerDay).
/// Usa Redis para acumular contadores diários por projeto (incrementados pelo TokenUsagePersistenceService).
/// Retorna 402 quando o orçamento diário é excedido.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semântica: SOFT budget.</b> <see cref="CheckAsync"/> bloqueia requisições novas
/// apenas DEPOIS que o contador Redis já ultrapassou o teto (read-then-compare).
/// Como o contador é incrementado post-LLM-call pelo <c>TokenUsagePersistenceService</c>,
/// requisições concorrentes próximas do limite podem passar juntas no check, rodar, e
/// somadas exceder o limite em alguns pontos percentuais.
/// </para>
/// <para>
/// <b>Implicação de produto:</b> o label visível na UI ("Limite diário") sugere
/// comportamento <i>hard</i>. Alinhar com Product Owner se a percepção do usuário precisa
/// ser ajustada (ex: "Uso diário estimado") ou se o comportamento deve virar hard.
/// </para>
/// <para>
/// <b>Para budget HARD</b> (impossível exceder), o design exigiria reserva upfront
/// — estimar <c>max_tokens</c> da request antes de chamar o LLM, decrementar atomicamente
/// com Lua script, executar, reconciliar com tokens reais depois. Esforço ~1 semana.
/// Ver GitHub issue #1 para discussão com produto.
/// </para>
/// </remarks>
public sealed class ProjectBudgetGuard
{
    private readonly IEfsRedisCache _cache;
    private readonly IProjectRepository _projectRepo;

    public ProjectBudgetGuard(IEfsRedisCache cache, IProjectRepository projectRepo)
    {
        _cache = cache;
        _projectRepo = projectRepo;
    }

    /// <summary>
    /// Verifica se o projeto está dentro do budget diário.
    /// Retorna (allowed, reason) — allowed=false inclui motivo para o 402.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> CheckAsync(
        string projectId, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        if (project?.Settings is null)
            return (true, null);

        var settings = project.Settings;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Check token budget
        if (settings.MaxTokensPerDay is > 0)
        {
            var tokensKey = $"budget:tokens:{projectId}:{today}";
            var tokensStr = await _cache.GetStringAsync(tokensKey);
            if (long.TryParse(tokensStr, out var tokens) && tokens >= settings.MaxTokensPerDay.Value)
            {
                return (false, $"Daily token budget exceeded ({tokens}/{settings.MaxTokensPerDay.Value} tokens).");
            }
        }

        // Check cost budget
        if (settings.MaxCostUsdPerDay is > 0)
        {
            var costKey = $"budget:cost:{projectId}:{today}";
            var costStr = await _cache.GetStringAsync(costKey);
            if (decimal.TryParse(costStr, out var cost) && cost >= settings.MaxCostUsdPerDay.Value)
            {
                return (false, $"Daily cost budget exceeded (${cost:F4}/${settings.MaxCostUsdPerDay.Value:F2} USD).");
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
