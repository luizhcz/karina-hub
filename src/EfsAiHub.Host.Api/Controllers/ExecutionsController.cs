using EfsAiHub.Host.Api.Endpoints.Polling;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/aihub/executions")]
[Produces("application/json")]
public class ExecutionsController : ControllerBase
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowEventBus _eventBus;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IExecutionDetailReader _detailReader;

    public ExecutionsController(
        IWorkflowService workflowService,
        IWorkflowEventBus eventBus,
        IWorkflowEventRepository eventRepo,
        IExecutionDetailReader detailReader)
    {
        _workflowService = workflowService;
        _eventBus = eventBus;
        _eventRepo = eventRepo;
        _detailReader = detailReader;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista execuções com filtros opcionais (todos os workflows)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? workflowId,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // Hard caps pra não drenar pool "reporting" nem buffer cache do Postgres.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var (items, total) = await _workflowService.GetAllExecutionsAsync(workflowId, status, from, to, page, pageSize, ct);
        return Ok(new
        {
            items = items.Select(ExecutionResponse.FromDomain),
            total,
            page,
            pageSize
        });
    }

    [HttpGet("{executionId}")]
    [SwaggerOperation(Summary = "Retorna os detalhes de uma execução incluindo os steps")]
    [ProducesResponseType(typeof(ExecutionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string executionId, CancellationToken ct)
    {
        var execution = await _workflowService.GetExecutionAsync(executionId, ct);
        if (execution is null) return NotFound();
        return Ok(ExecutionDetailResponse.FromDomain(execution));
    }

    [HttpDelete("{executionId}")]
    [SwaggerOperation(Summary = "Solicita cancelamento de uma execução em andamento")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(string executionId, CancellationToken ct)
    {
        await _workflowService.CancelExecutionAsync(executionId, ct);
        return Accepted(new { message = $"Cancelamento solicitado para execução '{executionId}'." });
    }

    [HttpGet("{executionId}/full")]
    [SwaggerOperation(Summary = "Retorna o dump completo de uma execução: metadata, nodes, tools e eventos")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFull(string executionId, CancellationToken ct)
    {
        var detail = await _detailReader.GetFullAsync(executionId, ct);
        if (detail is null) return NotFound();

        return Ok(new
        {
            execution = ExecutionDetailResponse.FromDomain(detail.Execution),
            nodes = detail.Nodes,
            tools = detail.Tools,
            events = detail.Events
        });
    }

    [HttpGet("{executionId}/events")]
    [SwaggerOperation(Summary = "Polling fallback HTTP — alternativa ao /stream pra clientes sem SSE")]
    [ProducesResponseType(typeof(EventPollingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events(
        string executionId,
        [FromQuery] long? since,
        [FromQuery] int? limit,
        [FromQuery] int? waitMs,
        CancellationToken ct = default)
    {
        var (s, l, w, err) = EventPollingValidation.Parse(since, limit, waitMs);
        if (err is not null) return BadRequest(new { error = err });

        var execution = await _workflowService.GetExecutionAsync(executionId, ct);
        if (execution is null) return NotFound();

        var response = await DbPollingHelper.PollAsync(
            readSince: async (cursor, max, innerCt) =>
            {
                var events = await _eventRepo.GetSinceAsync(executionId, cursor, max, innerCt);
                return events.Select(e => new EventPollingItem(
                    Seq: e.SequenceId,
                    Type: e.EventType,
                    Payload: DbPollingHelper.ToJsonElement(e.Payload),
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc), TimeSpan.Zero))).ToList();
            },
            isTerminalAsync: async (_) =>
            {
                var current = await _workflowService.GetExecutionAsync(executionId, ct);
                return current is null || IsTerminalStatus(current.Status);
            },
            since: s,
            limit: l,
            waitFor: w,
            ct: ct);

        return Ok(response);
    }

    internal static bool IsTerminalStatus(EfsAiHub.Core.Orchestration.Enums.WorkflowStatus status) =>
        status is EfsAiHub.Core.Orchestration.Enums.WorkflowStatus.Completed
              or EfsAiHub.Core.Orchestration.Enums.WorkflowStatus.Failed
              or EfsAiHub.Core.Orchestration.Enums.WorkflowStatus.Cancelled;

    [HttpGet("{executionId}/stream")]
    [SwaggerOperation(Summary = "Streaming SSE de eventos da execução em tempo real")]
    public async Task Stream(string executionId, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        await foreach (var evt in _eventBus.SubscribeAsync(executionId, ct))
        {
            var data = $"event: {evt.EventType}\ndata: {evt.Payload}\n\n";
            await Response.WriteAsync(data, ct);
            await Response.Body.FlushAsync(ct);

            if (evt.EventType is "workflow_completed" or "error")
                break;
        }
    }
}
