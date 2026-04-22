using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Messaging;

/// <summary>
/// Cobre o bug corrigido em 20472a6: sob subscribe/dispose/resubscribe em rajada,
/// a conn do pool SSE voltava em estado "Waiting" e o próximo subscriber batia
/// NpgsqlOperationInProgressException. Estes testes falhariam sem o fix
/// (await backgroundTask antes do DisposeAsync no finally).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class PgEventBusLifecycleTests(IntegrationWebApplicationFactory factory)
{
    private IWorkflowEventBus Bus =>
        factory.Services.GetRequiredService<IWorkflowEventBus>();

    private static WorkflowEventEnvelope MakeEvent(string executionId, string type = "workflow_completed")
        => new()
        {
            EventType = type,
            ExecutionId = executionId,
            Payload = "{}",
        };

    // ── 1. Subscribe → publish → cancel → resubscribe em rajada ─────────────

    [Fact]
    public async Task Subscribe_ResubscribeInTightLoop_DoesNotLeakConn()
    {
        // 20 iterações back-to-back no mesmo thread. Se a conn voltar poluída
        // ao pool, uma das iterações > 1 estoura.
        for (var i = 0; i < 20; i++)
        {
            var executionId = Guid.NewGuid().ToString();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Publica o evento terminal ANTES de subscrever — o replay do
            // histórico vai pegar e fazer yield break.
            await Bus.PublishAsync(executionId, MakeEvent(executionId), cts.Token);

            var seen = new List<string>();
            await foreach (var evt in Bus.SubscribeAsync(executionId, cts.Token))
            {
                seen.Add(evt.EventType);
                if (evt.EventType is "workflow_completed" or "error") break;
            }

            seen.Should().Contain("workflow_completed",
                $"iter {i} deveria ter recebido workflow_completed via replay");
        }
    }

    // ── 2. Subscribers concorrentes de execuções distintas ────────────────────

    [Fact]
    public async Task Subscribe_ConcurrentDistinctExecutions_AllComplete()
    {
        const int subscriberCount = 10;
        var executionIds = Enumerable.Range(0, subscriberCount)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();

        // Publica evento terminal em cada execution antes de subscrever.
        // Cada subscriber recebe apenas o seu próprio evento via replay.
        foreach (var execId in executionIds)
            await Bus.PublishAsync(execId, MakeEvent(execId));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        async Task<(string ExecId, List<string> Types)> RunSubscriber(string execId)
        {
            var types = new List<string>();
            await foreach (var evt in Bus.SubscribeAsync(execId, cts.Token))
            {
                types.Add(evt.EventType);
                if (evt.EventType is "workflow_completed" or "error") break;
            }
            return (execId, types);
        }

        var tasks = executionIds.Select(RunSubscriber).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(subscriberCount);
        foreach (var (execId, types) in results)
        {
            types.Should().Contain("workflow_completed",
                $"subscriber {execId} deveria ter recebido workflow_completed");
        }
    }

    // ── 3. Subscribe + cancel precoce + resubscribe ───────────────────────────

    [Fact]
    public async Task Subscribe_CanceledEarly_DoesNotBreakNextSubscribe()
    {
        var execA = Guid.NewGuid().ToString();
        var execB = Guid.NewGuid().ToString();

        // Subscribe em A com cancel precoce (antes de qualquer publish).
        // Força o finally a rodar enquanto a task background está em WaitAsync —
        // exatamente o cenário do bug original.
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            try
            {
                await foreach (var _ in Bus.SubscribeAsync(execA, cts.Token))
                {
                    // Não deve chegar aqui — cancelamento dispara antes.
                }
            }
            catch (OperationCanceledException) { /* esperado */ }
        }

        // Pequena folga para a conn voltar ao pool.
        await Task.Delay(100);

        // Novo subscribe em outra execution — se o fix estiver errado, a conn
        // do pool virá poluída e estouraria NpgsqlOperationInProgressException.
        using var ctsB = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Bus.PublishAsync(execB, MakeEvent(execB), ctsB.Token);

        var seen = new List<string>();
        await foreach (var evt in Bus.SubscribeAsync(execB, ctsB.Token))
        {
            seen.Add(evt.EventType);
            if (evt.EventType is "workflow_completed" or "error") break;
        }

        seen.Should().Contain("workflow_completed");
    }
}
