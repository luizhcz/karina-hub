using EfsAiHub.Core.Abstractions.Identity;
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
    private readonly IProjectContextAccessor _projectAccessor;

    public PersonaPromptTemplatesAdminController(
        IPersonaPromptTemplateRepository repo,
        IPersonaPromptTemplateCache cache,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        IProjectContextAccessor projectAccessor)
    {
        _repo = repo;
        _cache = cache;
        _audit = audit;
        _auditContext = auditContext;
        _projectAccessor = projectAccessor;
    }

    /// <summary>
    /// F5.5 — valida se o <paramref name="scope"/> do template pode ser acessado
    /// pelo project corrente. Retorna true quando:
    /// <list type="bullet">
    ///   <item>Scope é <c>global:*</c> (cross-project por design).</item>
    ///   <item>Scope é <c>agent:*</c> (cross-project por design — mesmo agent
    ///     pode ser referenciado em múltiplos projects; lookup do owner do
    ///     agent fica como enforcement futuro via HasQueryFilter que já existe
    ///     em AgentDefinitionRow).</item>
    ///   <item>Scope é <c>project:{currentProjectId}:*</c>.</item>
    /// </list>
    /// Retorna false quando scope é <c>project:{otherProjectId}:*</c> — admin
    /// do project A tentando enumerar template do project B.
    /// </summary>
    private bool IsScopeAccessibleByCurrentProject(string scope)
    {
        var currentProject = _projectAccessor.Current.ProjectId;

        if (scope.StartsWith("global:", StringComparison.Ordinal)) return true;
        if (scope.StartsWith("agent:", StringComparison.Ordinal)) return true;

        if (scope.StartsWith("project:", StringComparison.Ordinal))
        {
            // Extrai {pid} entre "project:" e o próximo ':'.
            var rest = scope.AsSpan("project:".Length);
            var colon = rest.IndexOf(':');
            if (colon <= 0) return false;
            var scopeProjectId = rest[..colon];
            return scopeProjectId.SequenceEqual(currentProject);
        }

        // Scopes malformados (não deveriam ter passado na validação do upsert).
        return false;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todos os templates cadastrados")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        // F5.5: filtra em memória por scope acessível ao project corrente.
        // Scopes cross-project (global, agent) sempre aparecem; scopes
        // project-aware só aparecem pro project dono.
        var items = all.Where(t => IsScopeAccessibleByCurrentProject(t.Scope)).ToList();

        // Read audit: consulta de templates é trilha auditável (LGPD +
        // compliance de prompt engineering — quem viu o que).
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptTemplate,
            "*"), ct);

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
        if (item is null) return NotFound();

        // F5.5: cross-project enumeration guard — admin do project A não pode
        // ler template cujo scope é do project B. Return 404 (não 403) pra não
        // vazar existência do recurso.
        if (!IsScopeAccessibleByCurrentProject(item.Scope)) return NotFound();

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptTemplate,
            id.ToString()), ct);

        return Ok(item);
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
            return BadRequest(new { error = "Scope inválido. Formatos aceitos: 'global:{userType}', 'agent:{agentId}:{userType}', 'project:{projectId}:{userType}' ou 'project:{projectId}:agent:{agentId}:{userType}' (userType ∈ {cliente, admin})." });

        // F5.5: impede admin de criar template com scope de outro project.
        if (!IsScopeAccessibleByCurrentProject(request.Scope))
            return BadRequest(new { error = $"Scope '{request.Scope}' não é acessível pelo project corrente." });

        var existing = await _repo.GetByScopeAsync(request.Scope, ct);
        var action = existing is null ? AdminAuditActions.Create : AdminAuditActions.Update;
        var before = existing is null ? null : AdminAuditContext.Snapshot(existing);

        var saved = await _repo.UpsertAsync(new PersonaPromptTemplate
        {
            Scope = request.Scope,
            Name = request.Name,
            Template = request.Template,
            // F9: UpdatedBy removido do domínio — actor vem do admin_audit_log.
        },
        // F5: CreatedBy da version fica null pelo mesmo motivo — actor
        // real da mudança vive no admin_audit_log com ResourceId={template.Id}.
        createdBy: null,
        changeReason: null,
        ct: ct);

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
        if (existing is null) return NotFound();

        // F5.5: cross-project guard — admin do project A não pode deletar
        // template do project B.
        if (!IsScopeAccessibleByCurrentProject(existing.Scope)) return NotFound();

        var deleted = await _repo.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        await _cache.InvalidateAsync(existing.Scope);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.PersonaPromptTemplate,
            id.ToString(),
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);

        return NoContent();
    }

    // ── F5: versionamento + rollback ─────────────────────────────────────

    [HttpGet("{id:int}/versions")]
    [SwaggerOperation(Summary = "Lista o histórico de versões do template (mais recente primeiro)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions(int id, CancellationToken ct)
    {
        var template = await _repo.GetByIdAsync(id, ct);
        if (template is null) return NotFound();

        // F5.5: cross-project guard — evita vazar histórico de template de
        // outro project via enumeração de IDs.
        if (!IsScopeAccessibleByCurrentProject(template.Scope)) return NotFound();

        var versions = await _repo.GetVersionsAsync(id, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptTemplate,
            $"{id}:versions"), ct);

        return Ok(new { template, versions, activeVersionId = template.ActiveVersionId });
    }

    [HttpPost("{id:int}/rollback")]
    [SwaggerOperation(Summary = "Rollback pra uma version específica — cria nova version (append-only) com o conteúdo da alvo")]
    [ProducesResponseType(typeof(PersonaPromptTemplate), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(
        int id,
        [FromQuery] Guid versionId,
        CancellationToken ct)
    {
        var before = await _repo.GetByIdAsync(id, ct);
        if (before is null) return NotFound();

        // F5.5: cross-project guard — impede rollback cross-project de um
        // template que o admin corrente não gerencia.
        if (!IsScopeAccessibleByCurrentProject(before.Scope)) return NotFound();

        var rolled = await _repo.RollbackAsync(id, versionId, createdBy: null, ct);
        if (rolled is null)
            return NotFound(new { error = $"VersionId '{versionId}' não pertence ao template #{id}." });

        await _cache.InvalidateAsync(rolled.Scope);

        // Action customizada "rollback" ainda não está em AdminAuditActions;
        // usar Update e gravar VersionId alvo em ChangeReason via payloadAfter.
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            AdminAuditResources.PersonaPromptTemplate,
            $"{id}:rollback:{versionId}",
            payloadBefore: AdminAuditContext.Snapshot(before),
            payloadAfter: AdminAuditContext.Snapshot(rolled)), ct);

        return Ok(rolled);
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
        // Níveis aceitos (match com cadeia do PersonaPromptComposer — ADR 003):
        //   global:{userType}
        //   agent:{agentId}:{userType}
        //   project:{projectId}:{userType}
        //   project:{projectId}:agent:{agentId}:{userType}
        if (scope == PersonaPromptTemplate.GlobalScope("cliente")) return true;
        if (scope == PersonaPromptTemplate.GlobalScope("admin")) return true;

        if (scope.StartsWith("project:", StringComparison.Ordinal))
        {
            // project:{projectId}[:agent:{agentId}]:{userType}
            var rest = scope.AsSpan("project:".Length);
            var firstColon = rest.IndexOf(':');
            if (firstColon <= 0) return false;
            var projectId = rest[..firstColon];
            var afterProject = rest[(firstColon + 1)..];
            if (projectId.IsEmpty) return false;

            // Variante "project:{pid}:agent:{aid}:{userType}"
            if (afterProject.StartsWith("agent:"))
            {
                var agentRest = afterProject["agent:".Length..];
                var lastColon = agentRest.LastIndexOf(':');
                if (lastColon <= 0) return false;
                var agentId = agentRest[..lastColon];
                var ut = agentRest[(lastColon + 1)..];
                if (agentId.IsEmpty) return false;
                return ut.SequenceEqual("cliente") || ut.SequenceEqual("admin");
            }

            // Variante "project:{pid}:{userType}"
            return afterProject.SequenceEqual("cliente") || afterProject.SequenceEqual("admin");
        }

        if (scope.StartsWith("agent:", StringComparison.Ordinal))
        {
            var rest = scope.AsSpan("agent:".Length);
            var lastColon = rest.LastIndexOf(':');
            if (lastColon <= 0) return false;
            var agentId = rest[..lastColon];
            var userType = rest[(lastColon + 1)..];
            if (agentId.IsEmpty) return false;
            return userType.SequenceEqual("cliente") || userType.SequenceEqual("admin");
        }

        return false;
    }
}
