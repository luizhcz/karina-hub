using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Host.Api.Chat.AgUi.Approval;
using EfsAiHub.Host.Api.Chat.AgUi.Handlers;
using EfsAiHub.Host.Api.Chat.AgUi.Models;
using EfsAiHub.Host.Api.Chat.AgUi.State;

namespace EfsAiHub.Host.Api.Chat.AgUi;

public static class AgUiEndpoints
{
    public static void MapAgUiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat/ag-ui")
            .AllowAnonymous();

        // Inicia run e stream AG-UI SSE
        group.MapPost("/stream", StreamAsync)
            .Produces(200, contentType: "text/event-stream");

        // Cancela run em andamento
        group.MapPost("/cancel", CancelAsync);

        // Resolve interação HITL (aprovação/rejeição inline sem abrir novo SSE)
        group.MapPost("/resolve-hitl", ResolveHitlAsync);

        // Reconexão com resync
        group.MapGet("/reconnect/{executionId}", ReconnectAsync)
            .Produces(200, contentType: "text/event-stream");
    }

    private static async Task StreamAsync(
        AgUiRunInput input,
        AgUiSseHandler sseHandler,
        AgUiStateManager stateManager,
        AgUiFrontendToolHandler frontendToolHandler,
        AgUiApprovalMiddleware approvalMiddleware,
        IConversationFacade facade,
        UserIdentityResolver identityResolver,
        HttpContext context,
        CancellationToken ct)
    {
        // 0. Processar respostas de aprovação pendentes (HITL via request_approval)
        approvalMiddleware.ProcessApprovals(input.Messages);

        // Mensagem efetiva: último Messages[role=user]
        var effectiveMessage = input.Messages?.LastOrDefault(m => m.Role == "user")?.Content;

        var hasToolMessages = input.Messages?.Any(m => m.Role == "tool") ?? false;
        var isHitlPure = string.IsNullOrWhiteSpace(effectiveMessage) && hasToolMessages;

        if (string.IsNullOrWhiteSpace(effectiveMessage) && !isHitlPure)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(
                new { error = "No user message provided." }, ct);
            return;
        }

        // HITL puro: approvals já foram processados acima — retornar sem novo stream
        if (isHitlPure)
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(
                new { hitlResolved = true, threadId = input.ThreadId }, ct);
            return;
        }

        var effectiveTools           = input.Tools;
        var effectiveState           = input.State;
        var clientRunId              = input.RunId;
        // workflowId: body tem prioridade; header x-efs-workflow-id como fallback (clientes AG-UI padrão)
        var effectiveWorkflowId      = input.WorkflowId
                                    ?? context.Request.Headers["x-efs-workflow-id"].FirstOrDefault();
        var effectivePredictiveState = input.PredictiveState;

        // 1. Resolver/criar conversa
        var threadId = input.ThreadId ?? Guid.NewGuid().ToString();

        // 2. Inicializar estado compartilhado
        var sharedState = await stateManager.GetOrCreateAsync(threadId, effectiveState);

        // 3. Registrar frontend tools (se declaradas)
        var frontendTools = frontendToolHandler.RegisterFrontendTools(effectiveTools);
        // TODO: injetar frontendTools no ChatOptions quando a integração com AgentFactory estiver pronta

        // 4. Buscar ou criar conversa e enviar mensagem via facade existente
        var identity = identityResolver.TryResolve(context.Request.Headers, out var identityError);
        string resolvedUserId;
        string resolvedUserType;
        if (identity is not null)
        {
            resolvedUserId = identity.UserId;
            resolvedUserType = identity.UserType;
        }
        else
        {
            // Fallback para JWT claim (endpoints AllowAnonymous)
            resolvedUserId = context.User.FindFirst("sub")?.Value ?? "anonymous";
            // Anônimos compartilhariam um bucket único de rate limit — discriminar por IP
            if (resolvedUserId == "anonymous")
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                resolvedUserId = $"anon:{ip}";
            }
            resolvedUserType = "default";
        }

        var session = await facade.GetAsync(threadId, ct);
        if (session is null)
        {
            var createResult = await facade.CreateAsync(
                explicitWorkflowId: effectiveWorkflowId, resolvedUserId, resolvedUserType,
                metadata: null, ct);

            if (createResult.Status != ConversationOperationStatus.Ok)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(
                    new { error = createResult.ErrorMessage }, ct);
                return;
            }

            session = createResult.Value!;
            threadId = session.ConversationId;
            // Re-create state with actual threadId
            sharedState = await stateManager.GetOrCreateAsync(threadId, effectiveState);
        }

        // 5. Enviar mensagem
        var messages = new List<ChatMessageInput> { new("user", effectiveMessage!) };
        var sendResult = await facade.SendMessagesAsync(
            session.ConversationId,
            resolvedUserId,
            messages, ct);

        if (sendResult.Status != ConversationOperationStatus.Ok)
        {
            context.Response.StatusCode = sendResult.Status switch
            {
                ConversationOperationStatus.NotFound => 404,
                ConversationOperationStatus.RateLimited => 429,
                _ => 400
            };
            await context.Response.WriteAsJsonAsync(
                new { error = sendResult.ErrorMessage }, ct);
            return;
        }

        var executionId = sendResult.Value?.ExecutionId;
        if (executionId is null)
        {
            // HITL resolved or no execution — just return success
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(new
            {
                threadId = session.ConversationId,
                hitlResolved = sendResult.Value?.HitlResolved
            }, ct);
            return;
        }

        // 6. Stream AG-UI SSE (propagate client runId if provided)
        await sseHandler.StreamAsync(
            context.Response,
            executionId,
            session.ConversationId,
            sharedState,
            ct,
            clientRunId: clientRunId);
    }

    private static async Task<IResult> CancelAsync(
        CancelRunRequest request,
        AgUiCancellationHandler handler,
        CancellationToken ct)
    {
        await handler.CancelAsync(request.ExecutionId, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Resolve uma interação HITL pendente sem abrir um novo SSE stream.
    /// O SSE stream original (que está aguardando) continua e receberá os eventos restantes.
    /// </summary>
    private static IResult ResolveHitlAsync(
        HitlResolveRequest request,
        AgUiApprovalMiddleware approvalMiddleware)
    {
        approvalMiddleware.ProcessApprovals(
        [
            new AgUiInputMessage("tool", request.Response, request.ToolCallId)
        ]);
        return Results.Ok(new { resolved = true, toolCallId = request.ToolCallId });
    }

    private static async Task ReconnectAsync(
        string executionId,
        AgUiReconnectionHandler handler,
        AgUiStateManager stateManager,
        IChatMessageRepository messageRepo,
        HttpContext context,
        CancellationToken ct)
    {
        var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        var threadId = context.Request.Headers["x-thread-id"].FirstOrDefault() ?? executionId;

        var sharedState = await stateManager.GetOrCreateAsync(threadId);

        await handler.HandleReconnectAsync(
            context.Response,
            executionId,
            threadId,
            lastEventId,
            sharedState,
            messageRepo,
            ct);
    }
}
