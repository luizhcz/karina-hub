using EfsAiHub.Host.Worker.Services;
using EfsAiHub.Host.Worker.Services.EventHandlers;
using EfsAiHub.Infra.Observability.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Workers;

[Trait("Category", "Unit")]
public class AgentHandoffEventHandlerTests
{
    private sealed class Fixture
    {
        public INodeExecutionRepository NodeRepo { get; } = Substitute.For<INodeExecutionRepository>();
        public IWorkflowEventBus EventBus { get; } = Substitute.For<IWorkflowEventBus>();
        public TokenBatcher TokenBatcher { get; }
        public NodeStateTracker Tracker { get; } = new();
        public AgentHandoffEventHandler Handler { get; }
        public WorkflowExecution Execution { get; } = new()
        {
            ExecutionId = "exec-1",
            WorkflowId = "wf-1",
            Status = Core.Orchestration.Enums.WorkflowStatus.Running
        };

        public Fixture()
        {
            TokenBatcher = new TokenBatcher(EventBus, NullLogger<TokenBatcher>.Instance, flushIntervalMs: 100_000);
            Handler = new AgentHandoffEventHandler(
                NodeRepo, EventBus, TokenBatcher,
                NullLogger<AgentHandoffEventHandler>.Instance);
        }
    }

    private static AgentResponseUpdateEvent BuildTokenEvt(string agentId, string text)
    {
        // AgentResponseUpdateEvent(string executorId, AgentResponseUpdate update).
        // AgentResponseUpdate.ToString() concatena o conteúdo — é o que o handler usa
        // via tokenEvt.Data?.ToString() (comportamento inalterado pela extração).
        var update = new AgentResponseUpdate(ChatRole.Assistant, text);
        return new AgentResponseUpdateEvent(agentId, update);
    }

    [Fact]
    public async Task Handle_PrimeiroToken_InicializaAgente_EmiteNodeStarted()
    {
        var f = new Fixture();
        var evt = BuildTokenEvt("agent-A", "hello");

        await f.Handler.HandleAsync(evt, f.Execution, f.Tracker, agentNames: null, CancellationToken.None);

        f.Tracker.CurrentAgentId.Should().Be("agent-A");
        await f.NodeRepo.Received().SetNodeAsync(
            Arg.Is<NodeExecutionRecord>(r => r.NodeId == "agent-A" && r.Status == "running"));
        // Publica "node_started" no bus — verificamos via event_type no envelope
        await f.EventBus.Received().PublishAsync(
            f.Execution.ExecutionId,
            Arg.Is<WorkflowEventEnvelope>(e => e.EventType == "node_started"));
    }

    [Fact]
    public async Task Handle_TrocaDeAgente_FinalizaPrevio_EmiteHandoff_InicializaNovo()
    {
        var f = new Fixture();
        await f.Handler.HandleAsync(BuildTokenEvt("agent-A", "oi"), f.Execution, f.Tracker, null, CancellationToken.None);
        // Reset para clareza das asserções sobre eventos do handoff
        f.EventBus.ClearReceivedCalls();
        f.NodeRepo.ClearReceivedCalls();

        await f.Handler.HandleAsync(BuildTokenEvt("agent-B", "oi"), f.Execution, f.Tracker, null, CancellationToken.None);

        f.Tracker.CurrentAgentId.Should().Be("agent-B");
        // Eventos esperados na ordem: node_completed (A) → handoff (A→B) → node_started (B)
        Received.InOrder(() =>
        {
            f.EventBus.PublishAsync(Arg.Any<string>(),
                Arg.Is<WorkflowEventEnvelope>(e => e.EventType == "node_completed"));
            f.EventBus.PublishAsync(Arg.Any<string>(),
                Arg.Is<WorkflowEventEnvelope>(e => e.EventType == "handoff"));
            f.EventBus.PublishAsync(Arg.Any<string>(),
                Arg.Is<WorkflowEventEnvelope>(e => e.EventType == "node_started"));
        });
        // Persistiu dois SetNodeAsync: completed do A e running do B
        await f.NodeRepo.Received(2).SetNodeAsync(Arg.Any<NodeExecutionRecord>());
    }

    [Fact]
    public async Task Handle_MesmoAgente_NaoEmiteHandoff_AcumulaToken()
    {
        var f = new Fixture();
        await f.Handler.HandleAsync(BuildTokenEvt("agent-A", "foo"), f.Execution, f.Tracker, null, CancellationToken.None);
        f.EventBus.ClearReceivedCalls();
        f.NodeRepo.ClearReceivedCalls();

        await f.Handler.HandleAsync(BuildTokenEvt("agent-A", "bar"), f.Execution, f.Tracker, null, CancellationToken.None);

        // Segundo token do MESMO agente não deve publicar node_started/handoff
        await f.EventBus.DidNotReceive().PublishAsync(
            Arg.Any<string>(),
            Arg.Is<WorkflowEventEnvelope>(e =>
                e.EventType == "node_started" || e.EventType == "handoff"));
        await f.NodeRepo.DidNotReceive().SetNodeAsync(Arg.Any<NodeExecutionRecord>());
    }

    [Fact]
    public async Task Handle_AgentNamesResolvidos_EmiteNomesNoPayload()
    {
        var f = new Fixture();
        var names = new Dictionary<string, string>
        {
            ["agent-A"] = "Alpha",
            ["agent-B"] = "Bravo"
        };
        await f.Handler.HandleAsync(BuildTokenEvt("agent-A", "x"), f.Execution, f.Tracker, names, CancellationToken.None);
        f.EventBus.ClearReceivedCalls();

        await f.Handler.HandleAsync(BuildTokenEvt("agent-B", "y"), f.Execution, f.Tracker, names, CancellationToken.None);

        // O payload do handoff deve conter toAgentName="Bravo"
        await f.EventBus.Received().PublishAsync(
            Arg.Any<string>(),
            Arg.Is<WorkflowEventEnvelope>(e =>
                e.EventType == "handoff" && e.Payload.Contains("Bravo")));
    }
}
