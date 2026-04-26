using System.Text.Json;
using EfsAiHub.Core.Abstractions.Blocklist;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Host.Api.Endpoints.Requests;
using EfsAiHub.Host.Api.Endpoints.Responses;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Guards;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints admin pra gestão de blocklist por projeto + inspeção do catálogo curado.
/// AdminGateMiddleware bloqueia acesso não-admin (todas as rotas são admin-scope).
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class BlocklistController : ControllerBase
{
    private readonly IBlocklistCatalogRepository _catalogRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly ICacheInvalidationBus? _cacheBus;
    private readonly BlocklistEngine? _engine;

    public BlocklistController(
        IBlocklistCatalogRepository catalogRepo,
        IProjectRepository projectRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        ICacheInvalidationBus? cacheBus = null,
        BlocklistEngine? engine = null)
    {
        _catalogRepo = catalogRepo;
        _projectRepo = projectRepo;
        _audit = audit;
        _auditContext = auditContext;
        _cacheBus = cacheBus;
        _engine = engine;
    }

    [HttpGet("/api/admin/blocklist/catalog")]
    [SwaggerOperation(
        Summary = "Inspeciona o catálogo curado de blocklist (read-only)",
        Description = "Retorna grupos e patterns ativos. Atualizações via db/seeds.sql + apply.sh pelo DBA.")]
    [ProducesResponseType(typeof(BlocklistCatalogResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var snapshot = await _catalogRepo.LoadAllAsync(ct);
        return Ok(BlocklistCatalogResponse.From(snapshot));
    }

    [HttpGet("/api/projects/{id}/blocklist")]
    [SwaggerOperation(
        Summary = "Retorna a config de blocklist do projeto (override sobre o catálogo)",
        Description = "Default = blocklist desabilitada. Use PUT pra atualizar.")]
    [ProducesResponseType(typeof(ProjectBlocklistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectBlocklist(string id, CancellationToken ct)
    {
        var project = await _projectRepo.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        var settings = project.Settings?.Blocklist ?? BlocklistSettings.Default;
        return Ok(new ProjectBlocklistResponse(id, settings));
    }

    [HttpPut("/api/projects/{id}/blocklist")]
    [SwaggerOperation(
        Summary = "Atualiza config de blocklist do projeto",
        Description = "Substitui inteiramente ProjectSettings.Blocklist. " +
                      "Invalida cache local imediato + cross-pod via PgNotify.")]
    [ProducesResponseType(typeof(ProjectBlocklistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProjectBlocklist(
        string id, [FromBody] UpdateBlocklistRequest request, CancellationToken ct)
    {
        var project = await _projectRepo.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        var newBlocklist = request.ToDomain();
        var beforeSnapshot = AdminAuditContext.Snapshot(project.Settings?.Blocklist ?? BlocklistSettings.Default);

        // ProjectSettings é record com `with` — preservar demais campos do settings.
        var settings = project.Settings ?? new ProjectSettings();
        project.Settings = settings with { Blocklist = newBlocklist };
        await _projectRepo.UpdateAsync(project, ct);

        // Invalida local imediato (pod atual) — outras requests neste pod já veem nova config.
        _engine?.InvalidateProject(id);

        // Cross-pod: outros pods recebem via subscribe do ICacheInvalidationBus e invalidam L1.
        // Best-effort: se bus indisponível, TTL de 5s do L1 serve como fallback.
        if (_cacheBus is not null)
            await _cacheBus.PublishInvalidateAsync(BlocklistEngine.ProjectInvalidationCacheName, id, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            AdminAuditResources.Blocklist,
            id,
            payloadBefore: beforeSnapshot,
            payloadAfter: AdminAuditContext.Snapshot(newBlocklist)), ct);

        return Ok(new ProjectBlocklistResponse(id, newBlocklist));
    }

    [HttpGet("/api/projects/{id}/blocklist/violations")]
    [SwaggerOperation(
        Summary = "Lista violações de blocklist do projeto",
        Description = "Query no admin_audit_log filtrado por Action='blocklist_violation'. " +
                      "Conteúdo cru NUNCA exposto — apenas hash + contexto ofuscado.")]
    [ProducesResponseType(typeof(IReadOnlyList<BlocklistViolationRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetViolations(
        string id,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // Clamp dupla camada: aqui em 100 + repository já clampa em 500.
        // Cap baixo aqui evita N+1 explosion mesmo se vários callers requisitarem em paralelo.
        var query = new AdminAuditQuery
        {
            ProjectId = id,
            ResourceType = AdminAuditResources.Blocklist,
            Action = AdminAuditActions.BlocklistViolation,
            From = from,
            To = to,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        };

        var entries = await _audit.QueryAsync(query, ct);
        var rows = entries.Select(MapViolationRow).ToList();
        return Ok(rows);
    }

    private static BlocklistViolationRow MapViolationRow(AdminAuditEntry entry)
    {
        // Payload é gravado pelo BlocklistChatClient.EmitMetricAndAuditAsync (PR 7).
        // Lê campos snake_case definidos lá; null-safe pra entries malformadas.
        var p = entry.PayloadAfter?.RootElement;
        return new BlocklistViolationRow(
            AuditId: entry.Id,
            DetectedAt: entry.Timestamp,
            UserId: entry.ActorUserId,
            AgentId: entry.ResourceId,
            Phase: GetString(p, "phase"),
            Category: GetString(p, "category"),
            PatternId: GetString(p, "pattern_id"),
            Action: GetString(p, "action_taken"),
            ContentHash: GetString(p, "content_hash"),
            ContextObfuscated: GetString(p, "context_obfuscated"));
    }

    private static string? GetString(JsonElement? root, string property)
    {
        if (root is not { ValueKind: JsonValueKind.Object } el) return null;
        if (!el.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
