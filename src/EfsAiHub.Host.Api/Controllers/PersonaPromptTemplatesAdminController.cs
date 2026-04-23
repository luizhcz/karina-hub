using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Personalization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD dos templates de prompt de persona + endpoint de preview ao vivo.
/// Scope: <c>global</c> (1 template default) ou <c>agent:{agentId}</c> (override).
/// Update in-place via <see cref="IPersonaPromptTemplateRepository.UpsertAsync"/>
/// — edição altera a mesma linha, audit trail vive em <c>admin_audit_log</c>.
/// </summary>
[ApiController]
[Route("api/admin/persona-templates")]
[Produces("application/json")]
public class PersonaPromptTemplatesAdminController : ControllerBase
{
    private readonly IPersonaPromptTemplateRepository _repo;
    private readonly IPersonaPromptTemplateCache _cache;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public PersonaPromptTemplatesAdminController(
        IPersonaPromptTemplateRepository repo,
        IPersonaPromptTemplateCache cache,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _repo = repo;
        _cache = cache;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os templates cadastrados")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await _repo.GetAllAsync(ct);
        // Incluímos as duas listas de placeholders (cliente vs admin) pra UI
        // renderizar o conjunto correto baseado no userType do template editado.
        return Ok(new
        {
            items,
            placeholders = new
            {
                client = PersonaPlaceholders.ForClient,
                admin = PersonaPlaceholders.ForAdmin,
            },
        });
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Retorna um template por ID")]
    [ProducesResponseType(typeof(PersonaPromptTemplate), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria ou atualiza um template por Scope (upsert)")]
    [ProducesResponseType(typeof(PersonaPromptTemplate), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert([FromBody] PersonaPromptTemplateUpsertRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Scope))
            return BadRequest(new { error = "Scope é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.Template))
            return BadRequest(new { error = "Template é obrigatório." });
        if (!IsValidScope(request.Scope))
            return BadRequest(new { error = "Scope inválido. Use 'global:cliente', 'global:admin', 'agent:{agentId}:cliente' ou 'agent:{agentId}:admin'." });

        var existing = await _repo.GetByScopeAsync(request.Scope, ct);
        var action = existing is null ? AdminAuditActions.Create : AdminAuditActions.Update;
        var before = existing is null ? null : AdminAuditContext.Snapshot(existing);

        var saved = await _repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = request.Scope,
            Name = request.Name,
            Template = request.Template,
            // UpdatedBy fica null — actor já é gravado no admin_audit_log pelo
            // AdminAuditLogger. Evita duplicar info em 2 fontes.
            UpdatedBy = null,
        }, ct);

        await _cache.InvalidateAsync(saved.Scope);

        await _audit.RecordAsync(_auditContext.Build(
            action,
            AdminAuditResources.PersonaPromptTemplate,
            saved.Id.ToString(),
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(saved)), ct);

        return Ok(saved);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Remove um template por ID")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        var deleted = await _repo.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        if (existing is not null)
            await _cache.InvalidateAsync(existing.Scope);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.PersonaPromptTemplate,
            id.ToString(),
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);

        return NoContent();
    }

    [HttpPost("preview")]
    [SwaggerOperation(Summary = "Renderiza um template com uma amostra de persona (não persiste)")]
    public IActionResult Preview([FromBody] PersonaPromptTemplatePreviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
            return BadRequest(new { error = "Template é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.UserType))
            return BadRequest(new { error = "UserType é obrigatório." });

        // Reusa a pura function de renderização — mesmo código rodando em produção.
        UserPersona sample = request.UserType switch
        {
            UserPersonaFactory.ClienteUserType => new ClientPersona(
                UserId: "preview",
                ClientName: request.Client?.ClientName,
                SuitabilityLevel: request.Client?.SuitabilityLevel,
                SuitabilityDescription: request.Client?.SuitabilityDescription,
                BusinessSegment: request.Client?.BusinessSegment,
                Country: request.Client?.Country,
                IsOffshore: request.Client?.IsOffshore ?? false),
            UserPersonaFactory.AdminUserType => new AdminPersona(
                UserId: "preview",
                Username: request.Admin?.Username,
                PartnerType: request.Admin?.PartnerType,
                Segments: request.Admin?.Segments ?? Array.Empty<string>(),
                Institutions: request.Admin?.Institutions ?? Array.Empty<string>(),
                IsInternal: request.Admin?.IsInternal ?? false,
                IsWm: request.Admin?.IsWm ?? false,
                IsMaster: request.Admin?.IsMaster ?? false,
                IsBroker: request.Admin?.IsBroker ?? false),
            _ => throw new ArgumentException($"UserType desconhecido: {request.UserType}"),
        };

        var rendered = PersonaTemplateRenderer.Render(request.Template, sample);
        return Ok(new { rendered, sample });
    }

    private static bool IsValidScope(string scope)
    {
        if (scope == PersonaPromptTemplate.GlobalScope("cliente")) return true;
        if (scope == PersonaPromptTemplate.GlobalScope("admin")) return true;
        // agent:{id}:{userType} — id não vazio, userType ∈ {cliente, admin}
        if (!scope.StartsWith("agent:", StringComparison.Ordinal)) return false;
        var rest = scope.AsSpan("agent:".Length);
        var lastColon = rest.LastIndexOf(':');
        if (lastColon <= 0) return false;
        var agentId = rest[..lastColon];
        var userType = rest[(lastColon + 1)..];
        if (agentId.IsEmpty) return false;
        return userType.SequenceEqual("cliente") || userType.SequenceEqual("admin");
    }
}
