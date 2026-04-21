using EfsAiHub.Host.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Workers;

[Trait("Category", "Unit")]
public class NodePersistenceServiceTests
{
    private static (NodePersistenceService service, INodeExecutionRepository nodeRepo, IWorkflowEventBus eventBus) Build()
    {
        var nodeRepo = Substitute.For<INodeExecutionRepository>();
        var eventBus = Substitute.For<IWorkflowEventBus>();

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(INodeExecutionRepository)).Returns(nodeRepo);
        sp.GetService(typeof(IWorkflowEventBus)).Returns(eventBus);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var service = new NodePersistenceService(factory, NullLogger<NodePersistenceService>.Instance);
        return (service, nodeRepo, eventBus);
    }

    private static NodePersistenceJob MakeJob(string executionId = "exec-1", string eventType = "node_completed") =>
        new(
            new NodeExecutionRecord { NodeId = "node-1", ExecutionId = executionId, Status = "completed" },
            executionId,
            eventType,
            "{}");

    // ── Processamento ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_UmItem_ChamaSetNodeAsync()
    {
        var (service, nodeRepo, _) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _ = service.StartAsync(cts.Token);
        service.Enqueue(MakeJob());

        await Task.Delay(200);
        cts.Cancel();

        await nodeRepo.Received(1).SetNodeAsync(Arg.Any<NodeExecutionRecord>());
    }

    [Fact]
    public async Task Enqueue_UmItem_PublicaEventoNoBus()
    {
        var (service, _, eventBus) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _ = service.StartAsync(cts.Token);
        service.Enqueue(MakeJob("exec-42", "node_completed"));

        await Task.Delay(200);
        cts.Cancel();

        await eventBus.Received(1).PublishAsync(
            "exec-42",
            Arg.Is<WorkflowEventEnvelope>(e => e.EventType == "node_completed" && e.ExecutionId == "exec-42"));
    }

    [Fact]
    public async Task Enqueue_MultiploItens_PersisteTodosEmSequencia()
    {
        var (service, nodeRepo, _) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _ = service.StartAsync(cts.Token);

        for (var i = 0; i < 5; i++)
            service.Enqueue(MakeJob($"exec-{i}"));

        await Task.Delay(500);
        cts.Cancel();

        await nodeRepo.Received(5).SetNodeAsync(Arg.Any<NodeExecutionRecord>());
    }

    [Fact]
    public async Task Enqueue_ErroNoPersist_ContinuaProcessando()
    {
        var (service, nodeRepo, _) = Build();

        // First call throws, second succeeds
        nodeRepo.SetNodeAsync(Arg.Any<NodeExecutionRecord>())
            .Returns(
                _ => { throw new InvalidOperationException("DB error"); },
                _ => Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = service.StartAsync(cts.Token);

        service.Enqueue(MakeJob("exec-fail"));
        service.Enqueue(MakeJob("exec-ok"));

        await Task.Delay(400);
        cts.Cancel();

        // Both were attempted
        await nodeRepo.Received(2).SetNodeAsync(Arg.Any<NodeExecutionRecord>());
    }

    // ── Writer property ────────────────────────────────────────────────────────

    [Fact]
    public void Writer_Exposto_NaoNulo()
    {
        var (service, _, _) = Build();

        service.Writer.Should().NotBeNull();
    }

    // ── Shutdown limpo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_SemItens_EncerraLimpo()
    {
        var (service, _, _) = Build();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        cts.Cancel();

        var stop = async () => await service.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
    }
}
