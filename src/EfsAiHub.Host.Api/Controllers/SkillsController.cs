using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Fase 3 — CRUD de Skills. Upsert dual-escreve snapshot imutável em skill_versions.
/// </summary>
[ApiController]
[Route("api/skills")]
[Produces("application/json")]
public class SkillsController : ControllerBase
{
    private readonly ISkillRepository _skills;
    private readonly ISkillVersionRepository _versions;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public SkillsController(
        ISkillRepository skills,
        ISkillVersionRepository versions,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _skills = skills;
        _versions = versions;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista todas as skills")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;
        var items = await _skills.GetAllAsync(page, pageSize, ct);
        var total = await _skills.CountAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtém uma skill por id")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var skill = await _skills.GetByIdAsync(id, ct);
        return skill is null ? NotFound() : Ok(skill);
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Cria ou atualiza uma skill (dual-write de snapshot imutável)")]
    public async Task<IActionResult> Upsert(string id, [FromBody] Skill skill, CancellationToken ct)
    {
        if (!string.Equals(id, skill.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Path id and body id must match.");

        var existing = await _skills.GetByIdAsync(id, ct);
        var action = existing is null ? AdminAuditActions.Create : AdminAuditActions.Update;
        var before = existing is null ? null : AdminAuditContext.Snapshot(existing);

        var saved = await _skills.UpsertAsync(skill, ct);
        await _audit.RecordAsync(_auditContext.Build(
            action,
            AdminAuditResources.Skill,
            saved.Id,
            payloadBefore: before,
            payloadAfter: AdminAuditContext.Snapshot(saved)), ct);
        return Ok(saved);
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Remove uma skill (não apaga skill_versions)")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var existing = await _skills.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        var deleted = await _skills.DeleteAsync(id, ct);
        if (!deleted) return NotFound();

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.Skill,
            id,
            payloadBefore: AdminAuditContext.Snapshot(existing)), ct);
        return NoContent();
    }

    [HttpGet("{id}/versions")]
    [SwaggerOperation(Summary = "Lista versões imutáveis de uma skill (DESC)")]
    public async Task<IActionResult> ListVersions(string id, CancellationToken ct)
        => Ok(await _versions.ListBySkillAsync(id, ct));
}
