using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using ExecutionContext = EfsAiHub.Core.Agents.Execution.ExecutionContext;

namespace EfsAiHub.Platform.Runtime.Hitl;

/// <summary>
/// Decorator que envolve qualquer <see cref="Executor{TIn, TOut}"/> com
/// HITL configurado declarativamente via <see cref="NodeHitlConfig"/>.
///
/// Preserva o Id do executor original para que as edges do grafo continuem
/// funcionando corretamente.
///
/// Comportamento:
///   when="before" → pausa antes de executar o nó (humano vê o input)
///   when="after"  → executa o nó, depois pausa (humano vê o output se showOutput=true)
///
/// Se o humano rejeitar, lança <see cref="HitlRejectedException"/> — capturada
/// pelo WorkflowRunnerService para marcar a execução como Failed.
/// </summary>
public sealed class HitlDecoratorExecutor : Executor<string, string>
{
    private readonly Executor<string, string> _inner;
    private readonly NodeHitlConfig _config;
    private readonly IHumanInteractionService _hitlService;
    private readonly IWorkflowEventBus _eventBus;

    public HitlDecoratorExecutor(
        Executor<string, string> inner,
        NodeHitlConfig config,
        IHumanInteractionService hitlService,
        IWorkflowEventBus eventBus)
        : base(inner.Id) // preserva identidade do nó para edge routing
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _hitlService = hitlService ?? throw new ArgumentNullException(nameof(hitlService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public override async ValueTask<string> HandleAsync(
        string input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_config.When, "before", StringComparison.OrdinalIgnoreCase))
        {
            await RequestApprovalAsync(contextData: input, cancellationToken);
        }

        var result = await _inner.HandleAsync(input, context, cancellationToken);

        if (string.Equals(_config.When, "after", StringComparison.OrdinalIgnoreCase))
        {
            var contextData = _config.ShowOutput ? result : input;
            await RequestApprovalAsync(contextData, cancellationToken);
        }

        return result;
    }

    private async Task RequestApprovalAsync(string contextData, CancellationToken ct)
    {
        var ctx = DelegateExecutor.Current.Value;
        var executionId = ctx?.ExecutionId ?? "unknown";
        var workflowId = ctx?.WorkflowId ?? "unknown";

        var interactionId = Guid.NewGuid().ToString();

        // Publicar evento hitl_required (mesmo formato do PixTestSetup)
        await _eventBus.PublishAsync(executionId, new WorkflowEventEnvelope
        {
            EventType = "hitl_required",
            ExecutionId = executionId,
            Payload = JsonSerializer.Serialize(
                new
                {
                    interactionId,
                    prompt = _config.Prompt,
                    question = _config.Prompt,
                    options = _config.Options,
                    timeoutSeconds = _config.TimeoutSeconds,
                    interactionType = _config.InteractionType.ToString()
                },
                JsonDefaults.Domain)
        }, ct);

        // Bloquear até resposta humana (ou timeout)
        var resolution = await _hitlService.RequestAsync(new HumanInteractionRequest
        {
            InteractionId = interactionId,
            ExecutionId = executionId,
            WorkflowId = workflowId,
            Prompt = _config.Prompt,
            Context = contextData,
            InteractionType = _config.InteractionType,
            Options = _config.Options?.ToArray(),
            TimeoutSeconds = _config.TimeoutSeconds
        }, ct);

        if (HitlResolutionClassifier.IsRejected(resolution))
            throw new HitlRejectedException(Id, resolution);
    }
}
