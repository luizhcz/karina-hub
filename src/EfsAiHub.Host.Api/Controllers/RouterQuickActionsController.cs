using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.RouterQuickActions;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.RouterQuickActions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD de atalhos do Router (Quick Actions). Cada atalho mapeia um Pattern
/// textual pra uma Intent — quando a mensagem do usuário bate no pattern, o
/// Router retorna a intent direto sem chamar o LLM (zero token usage).
///
/// <para>Escopo: cada projeto tem seus próprios atalhos por Router. Um Router
/// Visibility=global pode ter atalhos diferentes em projetos diferentes.</para>
/// </summary>
[ApiController]
[Route("api/aihub/agents/{routerId}/quick-actions")]
[Produces("application/json")]
public sealed class RouterQuickActionsController : ControllerBase
{
    private readonly IRouterQuickActionRepository _repo;
    private readonly IRouterQuickActionMatcher _matcher;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentRouterIntentLinkRepository _intentLinkRepo;
    private readonly IProjectContextAccessor _projectCtx;
    private readonly ITenantContextAccessor _tenantCtx;

    public RouterQuickActionsController(
        IRouterQuickActionRepository repo,
        IRouterQuickActionMatcher matcher,
        IAgentDefinitionRepository agentRepo,
        IAgentRouterIntentLinkRepository intentLinkRepo,
        IProjectContextAccessor projectCtx,
        ITenantContextAccessor tenantCtx)
    {
        _repo = repo;
        _matcher = matcher;
        _agentRepo = agentRepo;
        _intentLinkRepo = intentLinkRepo;
        _projectCtx = projectCtx;
        _tenantCtx = tenantCtx;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista atalhos do Router pra o projeto corrente. Cada item carrega displayText (label do botão na UI), pattern, intent e description.")]
    [ProducesResponseType(typeof(IReadOnlyList<QuickActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string routerId, CancellationToken ct)
    {
        var router = await EnsureRouterAsync(routerId, ct);
        if (router is null) return NotFound();

        var tenantId = _tenantCtx.Current.TenantId;
        var projectId = _projectCtx.Current.ProjectId;
        var entries = await _repo.ListByRouterAsync(routerId, tenantId, ct);
        var byProject = entries.Where(e => e.ProjectId == projectId).ToList();
        return Ok(byProject.Select(QuickActionResponse.From));
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um atalho. Pattern é normalizado (lowercase + collapsed whitespace) e pode terminar com ' *' pra wildcard. Intent precisa estar no pool do Router (agent_router_intents).")]
    [ProducesResponseType(typeof(QuickActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(string routerId, [FromBody] CreateRequest body, CancellationToken ct)
    {
        var router = await EnsureRouterAsync(routerId, ct);
        if (router is null) return NotFound();

        var (normalized, error) = ValidateAndNormalize(body.Pattern, body.DisplayText, body.Intent);
        if (error is not null) return BadRequest(new { error });

        if (!await IntentIsValidAsync(routerId, body.Intent, ct))
            return BadRequest(new { error = $"Intent '{body.Intent}' não está no pool do Router." });

        var tenantId = _tenantCtx.Current.TenantId;
        var projectId = _projectCtx.Current.ProjectId;

        try
        {
            var entity = new RouterQuickAction
            {
                Id = Guid.NewGuid().ToString("N"),
                RouterId = routerId,
                Pattern = normalized!,
                DisplayText = body.DisplayText.Trim(),
                Intent = body.Intent,
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
                ProjectId = projectId,
                TenantId = tenantId
            };
            var created = await _repo.CreateAsync(entity, ct);
            _matcher.InvalidateRouter(routerId, tenantId, projectId);
            return CreatedAtAction(nameof(List), new { routerId }, QuickActionResponse.From(created));
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_router_quick_actions") == true)
        {
            return Conflict(new { error = $"Já existe atalho com pattern '{normalized}' nesse Router/projeto." });
        }
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza pattern/displayText/intent/description do atalho.")]
    [ProducesResponseType(typeof(QuickActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string routerId, string id, [FromBody] CreateRequest body, CancellationToken ct)
    {
        var tenantId = _tenantCtx.Current.TenantId;
        var projectId = _projectCtx.Current.ProjectId;
        var existing = await _repo.GetByIdAsync(id, tenantId, ct);
        if (existing is null || existing.RouterId != routerId || existing.ProjectId != projectId)
            return NotFound();

        var (normalized, error) = ValidateAndNormalize(body.Pattern, body.DisplayText, body.Intent);
        if (error is not null) return BadRequest(new { error });

        if (!await IntentIsValidAsync(routerId, body.Intent, ct))
            return BadRequest(new { error = $"Intent '{body.Intent}' não está no pool do Router." });

        var updated = new RouterQuickAction
        {
            Id = existing.Id,
            RouterId = existing.RouterId,
            Pattern = normalized!,
            DisplayText = body.DisplayText.Trim(),
            Intent = body.Intent,
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            ProjectId = existing.ProjectId,
            TenantId = existing.TenantId,
            CreatedAt = existing.CreatedAt
        };
        var saved = await _repo.UpdateAsync(updated, ct);
        _matcher.InvalidateRouter(routerId, tenantId, projectId);
        return Ok(QuickActionResponse.From(saved));
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove o atalho.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string routerId, string id, CancellationToken ct)
    {
        var tenantId = _tenantCtx.Current.TenantId;
        var projectId = _projectCtx.Current.ProjectId;
        var existing = await _repo.GetByIdAsync(id, tenantId, ct);
        if (existing is null || existing.RouterId != routerId || existing.ProjectId != projectId)
            return NotFound();

        await _repo.DeleteAsync(id, tenantId, ct);
        _matcher.InvalidateRouter(routerId, tenantId, projectId);
        return NoContent();
    }

    private async Task<AgentDefinition?> EnsureRouterAsync(string routerId, CancellationToken ct)
    {
        var agent = await _agentRepo.GetByIdAsync(routerId, ct);
        if (agent is null) return null;
        if (agent.Type != AgentType.Router) return null;
        return agent;
    }

    private async Task<bool> IntentIsValidAsync(string routerId, string intent, CancellationToken ct)
    {
        var intents = await _intentLinkRepo.ListIntentIdsForAgentAsync(routerId, ct);
        return intents.Contains(intent, StringComparer.Ordinal);
    }

    /// <summary>
    /// Normaliza o pattern e valida sintaticamente. Retorna (normalized, null)
    /// no sucesso; (null, errorMsg) quando inválido.
    /// </summary>
    private static (string? Normalized, string? Error) ValidateAndNormalize(string? pattern, string? displayText, string? intent)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return (null, "displayText é obrigatório.");
        if (string.IsNullOrWhiteSpace(intent))
            return (null, "intent é obrigatório.");
        if (string.IsNullOrWhiteSpace(pattern))
            return (null, "pattern é obrigatório.");

        var normalized = QuickActionPatternMatcher.Normalize(pattern!);
        if (!QuickActionPatternMatcher.IsValidPattern(normalized))
            return (null, "pattern inválido — use apenas letras, dígitos, espaços e opcionalmente ' *' no fim.");
        return (normalized, null);
    }

    public sealed record CreateRequest(string Pattern, string DisplayText, string Intent, string? Description);

    public sealed record QuickActionResponse(
        string Id,
        string RouterId,
        string Pattern,
        string DisplayText,
        string Intent,
        string? Description,
        bool HasWildcard,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static QuickActionResponse From(RouterQuickAction a) => new(
            a.Id, a.RouterId, a.Pattern, a.DisplayText, a.Intent, a.Description,
            QuickActionPatternMatcher.HasWildcard(a.Pattern),
            a.CreatedAt, a.UpdatedAt);
    }
}
