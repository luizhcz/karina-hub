using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Chat.AgUi;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Tests.Unit.AgUi;

[Trait("Category", "Unit")]
public class AgUiEventMapperTests
{
    private readonly AgUiEventMapper _mapper = new();

    private static WorkflowEventEnvelope Envelope(string type, object payload) => new()
    {
        EventType = type,
        ExecutionId = "exec-1",
        Payload = JsonSerializer.Serialize(payload)
    };

    // ── TEXT_MESSAGE_CONTENT ───────────────────────────────────────────────────

    [Fact]
    public void TokenDelta_MapaParaTextMessageContent_ComDelta()
    {
        var env = Envelope("token_delta", new { content = "hello" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("TEXT_MESSAGE_CONTENT");
        evt.Delta.Should().NotBeNull();
        evt.Output.Should().BeNull();
    }

    // ── RUN_FINISHED ──────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowCompleted_MapaParaRunFinished_ComOutput()
    {
        var env = Envelope("workflow_completed", new { output = "resultado final" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("RUN_FINISHED");
        evt.Output.Should().Be("resultado final");
        evt.Delta.Should().BeNull();
    }

    // ── RUN_ERROR ─────────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowFailed_MapaParaRunError_ComErrorEErrorCode()
    {
        var env = Envelope("workflow_failed", new { message = "timeout", category = "Timeout" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("RUN_ERROR");
        evt.Error.Should().Be("timeout");
        evt.ErrorCode.Should().Be("Timeout");
        evt.Output.Should().BeNull();
    }

    [Fact]
    public void WorkflowCancelled_MapaParaRunError()
    {
        var env = Envelope("workflow_cancelled", new { message = "cancelado" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Single().Type.Should().Be("RUN_ERROR");
    }

    // ── RUN_STARTED ───────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowStarted_MapaParaRunStarted_ComRunIdEThreadId()
    {
        var env = Envelope("workflow_started", new { });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("RUN_STARTED");
        evt.RunId.Should().Be("run-1");
        evt.ThreadId.Should().Be("thread-1");
    }

    // ── TOOL_CALL_END ─────────────────────────────────────────────────────────

    [Fact]
    public void ToolCallCompleted_MapaParaToolCallEnd_SemResult()
    {
        var env = Envelope("tool_call_completed", new { invocationId = "inv-1", result = "data" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("TOOL_CALL_END");
        evt.ToolCallId.Should().Be("inv-1");
        evt.Result.Should().BeNull(); // AG-UI spec: TOOL_CALL_END não tem result
    }

    // ── STATE_SNAPSHOT ────────────────────────────────────────────────────────

    [Fact]
    public void StateSnapshot_MapaParaStateSnapshot()
    {
        var env = Envelope("state_snapshot", new { key = "value" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        var evt = events.Single();
        evt.Type.Should().Be("STATE_SNAPSHOT");
        evt.Snapshot.Should().NotBeNull();
    }

    // ── HITL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HitlRequired_MapaParaToolCallSequence()
    {
        var env = Envelope("hitl_required", new { interactionId = "int-1", question = "Confirma?" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(3);
        events.Select(e => e.Type).Should().BeEquivalentTo(
            new[] { "TOOL_CALL_START", "TOOL_CALL_ARGS", "TOOL_CALL_END" },
            opts => opts.WithStrictOrdering());
        events.All(e => e.ToolCallId == "int-1").Should().BeTrue();
    }

    // ── STEP_FINISHED (step_completed) ────────────────────────────────────────

    [Fact]
    public void StepCompleted_SemOutput_MapaApenasStepFinished()
    {
        var env = Envelope("step_completed", new { nodeId = "node-1" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(1);
        events[0].Type.Should().Be("STEP_FINISHED");
    }

    [Fact]
    public void StepCompleted_ComOutput_MapaStepFinishedMaisTextMessage()
    {
        var env = Envelope("step_completed", new { nodeId = "node-1", output = "resultado", wasStreamed = false });

        var events = _mapper.Map(env, "run-1", "thread-1");

        // STEP_FINISHED + TEXT_MESSAGE_START + TEXT_MESSAGE_CONTENT + TEXT_MESSAGE_END
        events.Should().HaveCount(4);
        events[0].Type.Should().Be("STEP_FINISHED");
        events[1].Type.Should().Be("TEXT_MESSAGE_START");
        events[2].Type.Should().Be("TEXT_MESSAGE_CONTENT");
        events[3].Type.Should().Be("TEXT_MESSAGE_END");
    }

    // ── Bifurcação por nodeType ───────────────────────────────────────────────

    [Fact]
    public void NodeStarted_NodeTypeAgent_EmiteStepStartedCanonico()
    {
        var env = Envelope("node_started", new { nodeId = "router", nodeType = "agent", agentName = "router-x" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(1);
        events[0].Type.Should().Be("STEP_STARTED");
        events[0].StepName.Should().Be("router-x");
    }

    [Fact]
    public void NodeStarted_NodeTypeExecutor_EmiteCustomLifecycleStarted()
    {
        var env = Envelope("node_started", new { nodeId = "post_processor", nodeType = "executor", functionName = "service_post_processor" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(1);
        events[0].Type.Should().Be("CUSTOM");
        events[0].CustomName.Should().Be("executor.lifecycle");

        var value = events[0].CustomValue!.Value;
        value.GetProperty("phase").GetString().Should().Be("started");
        value.GetProperty("nodeId").GetString().Should().Be("post_processor");
        value.GetProperty("functionName").GetString().Should().Be("service_post_processor");
    }

    [Fact]
    public void NodeCompleted_NodeTypeExecutor_NaoCriaTextMessageMesmoComOutput()
    {
        // Output não-streamed de executor não pode virar TEXT_MESSAGE_* — não é fala.
        var env = Envelope("node_completed", new
        {
            nodeId = "post_processor",
            nodeType = "executor",
            output = "{\"hasErrors\":false}",
            wasStreamed = false
        });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(1);
        events[0].Type.Should().Be("CUSTOM");
        events[0].CustomName.Should().Be("executor.lifecycle");
        events[0].CustomValue!.Value.GetProperty("phase").GetString().Should().Be("finished");
    }

    [Fact]
    public void NodeStarted_SemNodeType_FicaComStepStartedPorRetrocompat()
    {
        // Forward-compat: payload sem nodeType cai no caminho de agente (STEP_*).
        var env = Envelope("node_started", new { nodeId = "x", agentName = "agent-x" });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().HaveCount(1);
        events[0].Type.Should().Be("STEP_STARTED");
    }

    // ── Unknown events ────────────────────────────────────────────────────────

    [Fact]
    public void EventoDesconhecido_RetornaListaVazia()
    {
        var env = Envelope("evento_desconhecido_futuro", new { });

        var events = _mapper.Map(env, "run-1", "thread-1");

        events.Should().BeEmpty();
    }
}
