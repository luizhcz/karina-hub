using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.McpServers;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD de servidores MCP (Model Context Protocol). Agents referenciam por Id
/// e o AzureFoundryClientProvider resolve ServerLabel/ServerUrl/AllowedTools/Headers
/// em runtime toda vez que constrói o ChatOptions.
///
/// Sem validação de rede na criação — cadastrar um MCP offline é permitido.
/// Mudanças emitem linha em <c>aihub.admin_audit_log</c>.
/// </summary>
[ApiController]
[Route("api/admin/mcp-servers")]
[Produces("application/json")]
public class McpServersController : ControllerBase
{
    private readonly IMcpServerRepository _repo;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public McpServersController(
        IMcpServerRepository repo,
        ITenantContextAccessor tenantAccessor,
        IProjectContextAccessor projectAccessor,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _repo = repo;
        _tenantAccessor = tenantAccessor;
        _projectAccessor = projectAccessor;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista MCP servers do projeto atual (paginado)")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;
        var items = await _repo.GetAllAsync(page, pageSize, ct);
        var total = await _repo.CountAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtém um MCP server por Id")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var server = await _repo.GetByIdAsync(id, ct);
        return server is null ? NotFound() : Ok(server);
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria um novo MCP server no projeto atual")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] McpServer input, CancellationToken ct)
    {
        var validationError = Validate(input);
        if (validationError is not null) return BadRequest(new { error = validationError });

        var existing = await _repo.GetByIdAsync(input.Id, ct);
        if (existing is not null)
            return Conflict(new { error = $"MCP server '{input.Id}' já existe — use PUT para atualizar." });

        input.ProjectId = _projectAccessor.Current.ProjectId;
        var saved = await _repo.UpsertAsync(input, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Create,
            AdminAuditResources.McpServer,
            saved.Id,
            payloadAfter: AdminAuditContext.Snapshot(saved)), ct);

        return CreatedAtAction(nameof(Get), new { id = saved.Id }, saved);
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Atualiza um MCP server existente (não cria)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] McpServer input, CancellationToken ct)
    {
        if (!string.Equals(id, input.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Id na URL e no corpo devem coincidir." });

        var validationError = Validate(input);
        if (validationError is not null) return BadRequest(new { error = validationError });

        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        var before = AdminAuditContext.Snapshot(existing);

        // Preserva ProjectId/CreatedAt originais — mcp server não muda de projeto via update.
        input.ProjectId = existing.ProjectId;
        var preservedCreatedAt = existing.CreatedAt;
        var updated = new McpServer
        {
            Id = input.Id,
            Name = input.Name,
            Description = input.Description,
            ServerLabel = input.ServerLabel,
            ServerUrl = input.ServerUrl,
            AllowedTools = input.AllowedTools,
            Headers = input.Headers,
            RequireApproval = input.RequireApproval,
            ProjectId = existing.ProjectId,
            CreatedAt = preservedCreatedAt,
        };

        var saved = await _repo.UpsertAsync(updated, ct);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            AdminAuditResources.McpServer,
            saved.Id,
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(saved)), ct);

        return Ok(saved);
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove um MCP server. Agents que referenciam ficam com tool dangling.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        var deleted = await _repo.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.McpServer,
            id,
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);

        return NoContent();
    }

    private static string? Validate(McpServer input)
    {
        if (string.IsNullOrWhiteSpace(input.Id)) return "Id é obrigatório.";
        if (string.IsNullOrWhiteSpace(input.Name)) return "Name é obrigatório.";
        if (string.IsNullOrWhiteSpace(input.ServerLabel)) return "ServerLabel é obrigatório.";
        if (string.IsNullOrWhiteSpace(input.ServerUrl)) return "ServerUrl é obrigatório.";
        if (!Uri.TryCreate(input.ServerUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return "ServerUrl deve ser uma URL absoluta válida com esquema http ou https.";
        if (input.AllowedTools.Count == 0)
            return "AllowedTools precisa conter ao menos um item.";
        if (input.RequireApproval is not ("never" or "always"))
            return "RequireApproval inválido — valores aceitos: 'never', 'always'.";
        return null;
    }
}
