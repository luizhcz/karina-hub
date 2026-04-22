using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Consulta da trilha de auditoria de mudanças administrativas (CRUD em Project/Agent/
/// Workflow/Skill/ModelPricing). Tenant é sempre aplicado a partir do contexto da request —
/// o filtro de query nunca escapa do tenant atual.
/// </summary>
[ApiController]
[Route("api/admin/audit-log")]
[Produces("application/json")]
public class AdminAuditController : ControllerBase
{
    private readonly IAdminAuditLogger _audit;
    private readonly ITenantContextAccessor _tenantAccessor;

    public AdminAuditController(IAdminAuditLogger audit, ITenantContextAccessor tenantAccessor)
    {
        _audit = audit;
        _tenantAccessor = tenantAccessor;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista entradas de auditoria administrativa do tenant atual com paginação.")]
    public async Task<IActionResult> Query(
        [FromQuery] string? projectId,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] string? actorUserId,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new AdminAuditQuery
        {
            TenantId = _tenantAccessor.Current.TenantId, // tenant é sempre aplicado do contexto, não da query.
            ProjectId = projectId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserId = actorUserId,
            Action = action,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        };

        var items = await _audit.QueryAsync(query, ct);
        var total = await _audit.CountAsync(query, ct);

        return Ok(new
        {
            items = items.Select(Project).ToList(),
            total,
            page = query.Page,
            pageSize = query.PageSize,
        });
    }

    private static object Project(AdminAuditEntry e) => new
    {
        id = e.Id,
        tenantId = e.TenantId,
        projectId = e.ProjectId,
        actorUserId = e.ActorUserId,
        actorUserType = e.ActorUserType,
        action = e.Action,
        resourceType = e.ResourceType,
        resourceId = e.ResourceId,
        payloadBefore = ParseToElement(e.PayloadBefore),
        payloadAfter = ParseToElement(e.PayloadAfter),
        timestamp = e.Timestamp,
    };

    // JsonDocument não serializa bem direto no output do minimal pipeline; converte para JsonElement.
    // Retorno JsonElement? — null preserva omissão de campo no JSON de resposta.
    private static JsonElement? ParseToElement(JsonDocument? doc) => doc?.RootElement.Clone();
}
