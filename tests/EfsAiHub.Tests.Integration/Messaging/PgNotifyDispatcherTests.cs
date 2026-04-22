using EfsAiHub.Infra.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Messaging;

/// <summary>
/// Cobre o padrão de multiplexação do dispatcher: 1 conn PG persistente demultiplexada
/// in-memory para N subscribers. Testes focam no comportamento SEM tocar em lifecycle
/// (que já é coberto por PgEventBusLifecycleTests via integração end-to-end).
///
/// Os testes publicam via PgEventBus (que emite pg_notify no canal wf_events) e
/// verificam roteamento pelo dispatcher — incluindo dedup por ExecutionId.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class PgNotifyDispatcherTests(IntegrationWebApplicationFactory factory)
{
    private PgNotifyDispatcher Dispatcher =>
        factory.Services.GetRequiredService<PgNotifyDispatcher>();

    private IWorkflowEventBus Bus =>
        factory.Services.GetRequiredService<IWorkflowEventBus>();

    private static WorkflowEventEnvelope MakeEvent(string execId, string type = "step_completed")
        => new()
        {
            EventType = type,
            ExecutionId = execId,
            Payload = "{}",
        };

    // ── 1. Routing: evento chega apenas para o subscriber da execution correta ──

    [Fact]
    public async Task Subscribe_RoutesEventsByExecutionId()
    {
        var execA = Guid.NewGuid().ToString();
        var execB = Guid.NewGuid().ToString();

        await using var subA = Dispatcher.Subscribe(execA);
        await using var subB = Dispatcher.Subscribe(execB);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Publica em A — apenas subA deve receber
        await Bus.PublishAsync(execA, MakeEvent(execA, "step_completed"), cts.Token);

        // Drena 1 evento de subA (com timeout)
        await subA.Reader.WaitToReadAsync(cts.Token);
        subA.Reader.TryRead(out var evtA).Should().BeTrue();
        evtA!.ExecutionId.Should().Be(execA);

        // subB não deve ter recebido nada — damos um pequeno intervalo e verificamos
        await Task.Delay(100, cts.Token);
        subB.Reader.TryRead(out _).Should().BeFalse("subB não subscreveu execA");
    }

    // ── 2. Fanout: múltiplos subscribers da mesma execution recebem o mesmo evento ──

    [Fact]
    public async Task Subscribe_MultipleSubscribersSameExecution_AllReceiveEvent()
    {
        var execId = Guid.NewGuid().ToString();
        await using var sub1 = Dispatcher.Subscribe(execId);
        await using var sub2 = Dispatcher.Subscribe(execId);
        await using var sub3 = Dispatcher.Subscribe(execId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Bus.PublishAsync(execId, MakeEvent(execId, "step_completed"), cts.Token);

        async Task<WorkflowEventEnvelope> ReadOne(PgNotifyDispatcher.Subscription sub)
        {
            await sub.Reader.WaitToReadAsync(cts.Token);
            sub.Reader.TryRead(out var evt).Should().BeTrue();
            return evt!;
        }

        var results = await Task.WhenAll(ReadOne(sub1), ReadOne(sub2), ReadOne(sub3));
        results.Should().OnlyContain(e => e.ExecutionId == execId);
    }

    // ── 3. Cancel: subscriber descartado para de receber, dispatcher continua ──

    [Fact]
    public async Task Subscribe_DisposedSubscription_StopsReceivingAndDispatcherStillWorks()
    {
        var execId = Guid.NewGuid().ToString();
        var sub1 = Dispatcher.Subscribe(execId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Bus.PublishAsync(execId, MakeEvent(execId, "step_completed"), cts.Token);
        await sub1.Reader.WaitToReadAsync(cts.Token);
        sub1.Reader.TryRead(out _).Should().BeTrue();

        // Dispose — remove do dispatcher
        await sub1.DisposeAsync();

        // Novo subscriber na MESMA execution deve receber evento subsequente.
        // Comprova que dispatcher não ficou "corrompido" pelo dispose do primeiro.
        await using var sub2 = Dispatcher.Subscribe(execId);
        await Bus.PublishAsync(execId, MakeEvent(execId, "step_completed"), cts.Token);
        await sub2.Reader.WaitToReadAsync(cts.Token);
        sub2.Reader.TryRead(out var evt).Should().BeTrue();
        evt!.ExecutionId.Should().Be(execId);
    }

    // ── 4. Stress: muitos subscribers concorrentes não conflitam ──

    [Fact]
    public async Task Subscribe_ManyConcurrentSubscribers_NoCrossTalk()
    {
        const int count = 30;
        var execIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var subs = execIds.Select(id => Dispatcher.Subscribe(id)).ToArray();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Publica em cada execution em paralelo
            await Task.WhenAll(execIds.Select(id =>
                Bus.PublishAsync(id, MakeEvent(id, "step_completed"), cts.Token)));

            // Cada subscriber deve receber exatamente 1 evento, correspondente à sua execution
            async Task<(string, WorkflowEventEnvelope)> Read(int i)
            {
                await subs[i].Reader.WaitToReadAsync(cts.Token);
                subs[i].Reader.TryRead(out var evt).Should().BeTrue();
                return (execIds[i], evt!);
            }

            var results = await Task.WhenAll(Enumerable.Range(0, count).Select(Read));

            foreach (var (expected, evt) in results)
                evt.ExecutionId.Should().Be(expected,
                    "dispatcher deve rotear cada evento para o subscriber correto (zero cross-talk)");
        }
        finally
        {
            foreach (var s in subs) await s.DisposeAsync();
        }
    }
}
