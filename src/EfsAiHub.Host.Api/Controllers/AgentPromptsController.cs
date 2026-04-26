using EfsAiHub.Host.Api.Models.Requests;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/agents/{agentId}/prompts")]
[Produces("application/json")]
public class AgentPromptsController : ControllerBase
{
    private readonly IAgentPromptRepository _promptRepo;
    private readonly IAgentDefinitionRepository _agentRepo;

    public AgentPromptsController(
        IAgentPromptRepository promptRepo,
        IAgentDefinitionRepository agentRepo)
    {
        _promptRepo = promptRepo;
        _agentRepo = agentRepo;
    }

    /// <summary>Lista todas as versões de prompt do agente.</summary>
    [HttpGet]
    [SwaggerOperation(Summary = "Lista versões de prompt do agente")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListVersions(string agentId, CancellationToken ct)
    {
        if (!await _agentRepo.ExistsAsync(agentId, ct))
            return NotFound($"Agente '{agentId}' não encontrado.");

        var versions = await _promptRepo.ListVersionsAsync(agentId, ct);
        return Ok(versions);
    }

    /// <summary>Retorna o conteúdo do prompt ativo (versão master).</summary>
    [HttpGet("active")]
    [SwaggerOperation(Summary = "Retorna o prompt ativo do agente")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActive(string agentId, CancellationToken ct)
    {
        if (!await _agentRepo.ExistsAsync(agentId, ct))
            return NotFound($"Agente '{agentId}' não encontrado.");

        var prompt = await _promptRepo.GetActivePromptAsync(agentId, ct);
        if (prompt is null)
            return NotFound($"Nenhuma versão ativa definida para o agente '{agentId}'.");

        return Ok(new { content = prompt });
    }

    /// <summary>Grava ou atualiza uma versão de prompt.</summary>
    [HttpPost]
    [SwaggerOperation(Summary = "Grava ou atualiza uma versão de prompt")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveVersion(
        string agentId,
        [FromBody] SavePromptVersionRequest request,
        CancellationToken ct)
    {
        if (!await _agentRepo.ExistsAsync(agentId, ct))
            return NotFound($"Agente '{agentId}' não encontrado.");

        if (string.IsNullOrWhiteSpace(request.VersionId))
            return BadRequest("'versionId' é obrigatório.");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("'content' é obrigatório.");

        try
        {
            await _promptRepo.SaveVersionAsync(agentId, request.VersionId, request.Content, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        return CreatedAtAction(nameof(ListVersions), new { agentId }, new
        {
            agentId,
            versionId = request.VersionId
        });
    }

    /// <summary>Move o ponteiro master para uma versão existente.</summary>
    [HttpPut("master")]
    [SwaggerOperation(Summary = "Define qual versão é a ativa (master)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMaster(
        string agentId,
        [FromBody] SetMasterRequest request,
        CancellationToken ct)
    {
        if (!await _agentRepo.ExistsAsync(agentId, ct))
            return NotFound($"Agente '{agentId}' não encontrado.");

        if (string.IsNullOrWhiteSpace(request.VersionId))
            return BadRequest("'versionId' é obrigatório.");

        try
        {
            await _promptRepo.SetMasterAsync(agentId, request.VersionId, ct);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }

        return Ok(new { agentId, master = request.VersionId });
    }

    /// <summary>
    /// Restaura o master do agente para uma versão "original" cujo conteúdo é o
    /// <c>Instructions</c> atual do agente. Mantém a invariante "sempre 1 versão ativa".
    /// </summary>
    [HttpDelete("master")]
    [SwaggerOperation(Summary = "Restaura o master para o prompt original do agente")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearMaster(string agentId, CancellationToken ct)
    {
        var agent = await _agentRepo.GetByIdAsync(agentId, ct);
        if (agent is null)
            return NotFound($"Agente '{agentId}' não encontrado.");

        await _promptRepo.RestoreOriginalAsync(agentId, agent.Instructions ?? string.Empty, ct);
        return NoContent();
    }

    /// <summary>Remove uma versão de prompt (não pode ser a ativa).</summary>
    [HttpDelete("{versionId}")]
    [SwaggerOperation(Summary = "Remove uma versão de prompt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteVersion(
        string agentId, string versionId, CancellationToken ct)
    {
        if (!await _agentRepo.ExistsAsync(agentId, ct))
            return NotFound($"Agente '{agentId}' não encontrado.");

        try
        {
            await _promptRepo.DeleteVersionAsync(agentId, versionId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }

        return NoContent();
    }
}
