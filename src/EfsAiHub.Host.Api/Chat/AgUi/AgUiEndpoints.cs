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

    /// <summary>
    /// Endpoint principal AG-UI. Aceita extensão proprietária <c>actor</c> em messages
    /// (ver <c>docs/adr/0014-actor-robot-trust-model.md</c>): quando a última mensagem
    /// do batch tem <c>actor=robot</c>, o backend persiste sem disparar workflow e
    /// responde com SSE sintético. Trust model: confia no body sem auth, boundary
    /// fica no proxy upstream.
    /// </summary>
    private static async Task StreamAsync(
        AgUiRunInput input,
        AgUiSseHandler sseHandler,
        AgUiStateManager stateManager,
        AgUiFrontendToolHandler frontendToolHandler,
        AgUiApprovalMiddleware approvalMiddleware,
        IConversationFacade facade,
        IChatMessageRepository messageRepo,
        IHumanInteractionService hitlService,
        ILogger<AgUiSseHandler> logger,
        UserIdentityResolver identityResolver,
        HttpContext context,
        CancellationToken ct)
    {
        // 0a. Validar campo actor (extensão proprietária aditiva à spec AG-UI; ver ADR 0014).
        //     Trust model: backend não autentica o valor — proxy upstream é o boundary.
        //     Mas validação de payload (sintática/semântica) acontece sempre pra evitar
        //     spoofing acidental por bug de cliente.
        var actorValidationError = ValidateActorField(input.Messages);
        if (actorValidationError is not null)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = actorValidationError }, ct);
            return;
        }

        // 0. Resolver identidade (precisamos dela antes de ProcessApprovalsAsync para gravar ResolvedBy).
        //    Fallback para JWT sub-claim quando headers custom não estão presentes.
        var earlyIdentity = identityResolver.TryResolve(context.Request.Headers, out _);
        var hitlResolvedBy = earlyIdentity?.UserId
            ?? context.User.FindFirst("sub")?.Value
            ?? "anonymous";

        // 1. Processar respostas de aprovação pendentes (HITL via request_approval)
        await approvalMiddleware.ProcessApprovalsAsync(input.Messages, hitlResolvedBy, ct);

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

        // 5. Detectar actor=robot na última mensagem (já validado acima — só pode estar
        //    na última posição). Quando presente, propaga via ChatMessageInput.Actor pro
        //    domínio fazer short-circuit em ConversationService.SendMessagesAsync.
        var lastInputMsg = input.Messages?[^1];
        var isRobotTurn = string.Equals(
            lastInputMsg?.Actor?.Trim(),
            "robot",
            StringComparison.OrdinalIgnoreCase);

        // Validação semântica: HITL pendente + actor=robot é erro de cliente. HITL deve ser
        // resolvido via /resolve-hitl, não via /stream com actor=robot. Robot durante turn
        // humano em curso (sem HITL) é OK — ConversationService.SendMessagesAsync registra
        // sem cancelar a execução em andamento (ver Messaging.cs).
        if (isRobotTurn && !string.IsNullOrEmpty(session.ActiveExecutionId))
        {
            var pendingHitl = hitlService.GetPendingForExecution(session.ActiveExecutionId);
            if (pendingHitl is not null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "actor=robot não pode resolver HITL pendente — use POST /api/chat/ag-ui/resolve-hitl."
                }, ct);
                return;
            }
        }

        var actorEnum = isRobotTurn ? Actor.Robot : Actor.Human;
        var messages = new List<ChatMessageInput>
        {
            new("user", effectiveMessage!, Actor: actorEnum)
        };
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

        // 6a. Robot turn — short-circuit emite SSE sintético sem disparar workflow.
        if (isRobotTurn && executionId is null && sendResult.Value?.PersistedMessages is { } persisted)
        {
            EfsAiHub.Infra.Observability.MetricsRegistry.RobotMessagesPersisted.Add(1);
            logger.LogInformation(
                "[AgUi] actor=robot persistido sem disparo de workflow — convId='{ConvId}', messageId='{MsgId}'.",
                session.ConversationId,
                persisted.LastOrDefault(m => m.Actor == Actor.Robot)?.MessageId);

            var runId = clientRunId ?? Guid.NewGuid().ToString();
            await sseHandler.StreamRobotPersistedAsync(
                context.Response,
                runId,
                session.ConversationId,
                persisted,
                sharedState,
                messageRepo,
                ct);
            return;
        }

        // 6b. Sem execução e sem ser robot — HITL resolved ou outro caminho ortogonal.
        if (executionId is null)
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(new
            {
                threadId = session.ConversationId,
                hitlResolved = sendResult.Value?.HitlResolved
            }, ct);
            return;
        }

        // 6c. Stream AG-UI SSE normal (propagate client runId if provided)
        await sseHandler.StreamAsync(
            context.Response,
            executionId,
            session.ConversationId,
            sharedState,
            ct,
            clientRunId: clientRunId);
    }

    /// <summary>
    /// Valida o campo <c>actor</c> nas mensagens AG-UI:
    /// <list type="number">
    ///   <item>Campo ausente (null) é ok — vira default human.</item>
    ///   <item>Campo presente mas vazio/whitespace é misuse — 400 explícito (sem silent default,
    ///   coerência com a rejeição de strings desconhecidas).</item>
    ///   <item>Trim aplicado antes do compare pra ser amigável a clientes que mandam
    ///   <c>" robot "</c>; conteúdo desconhecido pós-trim → 400.</item>
    ///   <item><c>actor="robot"</c> só pode aparecer na ÚLTIMA mensagem do batch — robot
    ///   "fecha turno". No meio do array é misuse de cliente.</item>
    /// </list>
    /// Retorna mensagem de erro se inválido; null se OK.
    /// </summary>
    private static string? ValidateActorField(IReadOnlyList<AgUiInputMessage>? messages)
    {
        if (messages is null || messages.Count == 0) return null;

        for (int i = 0; i < messages.Count; i++)
        {
            var raw = messages[i].Actor;
            if (raw is null) continue;  // ausência do campo → default human

            var actor = raw.Trim();
            if (actor.Length == 0)
            {
                return $"actor inválido em messages[{i}]: vazio/whitespace. Omita o campo ou use \"human\"/\"robot\".";
            }

            if (!actor.Equals("human", StringComparison.OrdinalIgnoreCase)
                && !actor.Equals("robot", StringComparison.OrdinalIgnoreCase))
            {
                return $"actor inválido em messages[{i}]: '{raw}'. Valores aceitos: \"human\" ou \"robot\".";
            }

            // robot só pode estar na última posição
            if (actor.Equals("robot", StringComparison.OrdinalIgnoreCase) && i != messages.Count - 1)
            {
                return "actor=\"robot\" só é permitido na última mensagem do batch (fecha turno sem disparar workflow).";
            }
        }

        return null;
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
    private static async Task<IResult> ResolveHitlAsync(
        HitlResolveRequest request,
        AgUiApprovalMiddleware approvalMiddleware,
        UserIdentityResolver identityResolver,
        HttpContext context,
        CancellationToken ct)
    {
        var identity = identityResolver.TryResolve(context.Request.Headers, out _);
        var resolvedBy = identity?.UserId
            ?? context.User.FindFirst("sub")?.Value
            ?? "anonymous";

        await approvalMiddleware.ProcessApprovalsAsync(
        [
            new AgUiInputMessage("tool", request.Response, request.ToolCallId)
        ], resolvedBy, ct);
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
