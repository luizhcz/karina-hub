using System.Text.Json;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using ProviderCredentials = EfsAiHub.Core.Abstractions.Projects.ProviderCredentials;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Produces("application/json")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _repo;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly HashSet<string> _adminAccountIds;
    private readonly UserIdentityResolver _identityResolver;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public ProjectsController(
        IProjectRepository repo,
        ITenantContextAccessor tenantAccessor,
        IOptions<AdminOptions> adminOptions,
        UserIdentityResolver identityResolver,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _repo = repo;
        _tenantAccessor = tenantAccessor;
        _adminAccountIds = new HashSet<string>(adminOptions.Value.AccountIds, StringComparer.Ordinal);
        _identityResolver = identityResolver;
        _audit = audit;
        _auditContext = auditContext;
    }

    private bool IsAdmin()
    {
        if (_adminAccountIds.Count == 0) return true; // gate desabilitado (dev/test)
        var identity = _identityResolver.TryResolve(HttpContext.Request.Headers, out _);
        return identity != null && _adminAccountIds.Contains(identity.UserId);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um projeto")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var tenantId = _tenantAccessor.Current.TenantId;
        // Create() valida invariantes de domínio — lança DomainException (mapeado para 400)
        // se Id/TenantId/Name vazios ou budget com valores negativos.
        var project = Project.Create(
            id: Guid.NewGuid().ToString("N"),
            name: request.Name,
            tenantId: tenantId,
            description: request.Description,
            settings: MapSettings(request.Settings),
            llmConfig: MapLlmConfig(request.LlmConfig),
            budget: request.Budget.HasValue
                ? JsonDocument.Parse(request.Budget.Value.GetRawText())
                : null);

        await _repo.CreateAsync(project, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Create,
            AdminAuditResources.Project,
            project.Id,
            payloadAfter: AdminAuditContext.Snapshot(ProjectResponse.From(project))), ct);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ProjectResponse.From(project));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista projetos do tenant")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = _tenantAccessor.Current.TenantId;
        var projects = await _repo.GetByTenantAsync(tenantId, ct);

        var result = projects.AsEnumerable();
        if (!IsAdmin())
            result = result.Where(p => !p.Id.Equals("default", StringComparison.OrdinalIgnoreCase));

        return Ok(result.Select(ProjectResponse.From));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca um projeto por ID")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (id.Equals("default", StringComparison.OrdinalIgnoreCase) && !IsAdmin())
            return NotFound();

        var project = await _repo.GetByIdAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(ProjectResponse.From(project));
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza um projeto")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await _repo.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        var before = AdminAuditContext.Snapshot(ProjectResponse.From(project));

        if (request.Name is not null) project.Name = request.Name;
        if (request.Description is not null) project.Description = request.Description;
        if (request.Settings is not null) project.Settings = MapSettings(request.Settings);
        if (request.LlmConfig is not null)
        {
            var newConfig = MapLlmConfig(request.LlmConfig);
            // Se o novo config omite ou zera o ApiKey de um provider, preserva o valor existente
            // para que o frontend possa editar Endpoint sem perder a chave já configurada.
            if (newConfig is not null && project.LlmConfig?.Credentials is { Count: > 0 } existing)
            {
                foreach (var (provider, existingCred) in existing)
                {
                    if (newConfig.Credentials.TryGetValue(provider, out var newCred)
                        && string.IsNullOrEmpty(newCred.ApiKey)
                        && !string.IsNullOrEmpty(existingCred.ApiKey))
                    {
                        newConfig.Credentials[provider] = newCred with { ApiKey = existingCred.ApiKey };
                    }
                }
            }
            project.LlmConfig = newConfig;
        }
        if (request.Budget.HasValue)
            project.Budget = JsonDocument.Parse(request.Budget.Value.GetRawText());

        await _repo.UpdateAsync(project, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            AdminAuditResources.Project,
            project.Id,
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(ProjectResponse.From(project))), ct);
        return Ok(ProjectResponse.From(project));
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove um projeto")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (id == "default")
            return BadRequest("Cannot delete the default project.");

        var project = await _repo.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        var before = AdminAuditContext.Snapshot(ProjectResponse.From(project));
        await _repo.DeleteAsync(id, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.Project,
            id,
            payloadBefore: before), ct);
        return NoContent();
    }

    private static ProjectLlmConfig? MapLlmConfig(ProjectLlmConfigInput? input)
    {
        if (input is null) return null;
        var creds = new Dictionary<string, ProviderCredentials>();
        foreach (var (provider, c) in input.Credentials ?? [])
            creds[provider.ToUpperInvariant()] = new ProviderCredentials { ApiKey = c.ApiKey, Endpoint = c.Endpoint };
        return new ProjectLlmConfig { Credentials = creds, DefaultModel = input.DefaultModel, DefaultProvider = input.DefaultProvider };
    }

    private static ProjectSettings MapSettings(ProjectSettingsInput? input)
    {
        if (input is null) return new ProjectSettings();

        return new ProjectSettings
        {
            DefaultProvider = input.DefaultProvider,
            DefaultModel = input.DefaultModel,
            DefaultTemperature = input.DefaultTemperature,
            MaxTokensPerDay = input.MaxTokensPerDay,
            MaxCostUsdPerDay = input.MaxCostUsdPerDay,
            MaxConcurrentExecutions = input.MaxConcurrentExecutions,
            MaxRequestsPerMinute = input.MaxRequestsPerMinute,
            MaxConversationsPerUser = input.MaxConversationsPerUser,
            HitlEnabled = input.HitlEnabled ?? true,
            BackgroundResponsesEnabled = input.BackgroundResponsesEnabled ?? true,
            MaxSandboxTokensPerDay = input.MaxSandboxTokensPerDay ?? 50_000
        };
    }
}
