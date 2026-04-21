namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class WorkflowExecutionStateTests
{
    [Fact]
    public void CriaExecution_StatusPadraoEPending()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-1",
            WorkflowId = "wf-1"
        };

        exec.Status.Should().Be(WorkflowStatus.Pending);
        exec.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        exec.CompletedAt.Should().BeNull();
        exec.Output.Should().BeNull();
        exec.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Completa_PreencheOutputECompletedAt()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-1",
            WorkflowId = "wf-1"
        };

        exec.Status = WorkflowStatus.Completed;
        exec.Output = "resultado final";
        exec.CompletedAt = DateTime.UtcNow;

        exec.Status.Should().Be(WorkflowStatus.Completed);
        exec.Output.Should().Be("resultado final");
        exec.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Falha_PreencheErrorMessageECategory()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-2",
            WorkflowId = "wf-1"
        };

        exec.Status = WorkflowStatus.Failed;
        exec.ErrorMessage = "LLM timeout";
        exec.ErrorCategory = ErrorCategory.Timeout;

        exec.Status.Should().Be(WorkflowStatus.Failed);
        exec.ErrorMessage.Should().Be("LLM timeout");
        exec.ErrorCategory.Should().Be(ErrorCategory.Timeout);
    }

    [Fact]
    public void Pausa_MantemCheckpointKey()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-3",
            WorkflowId = "wf-1"
        };

        exec.Status = WorkflowStatus.Paused;
        exec.CheckpointKey = "ckpt-abc";

        exec.Status.Should().Be(WorkflowStatus.Paused);
        exec.CheckpointKey.Should().Be("ckpt-abc");
    }

    [Fact]
    public void Cancela_StatusCancelled()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-4",
            WorkflowId = "wf-1"
        };

        exec.Status = WorkflowStatus.Cancelled;

        exec.Status.Should().Be(WorkflowStatus.Cancelled);
    }

    [Fact]
    public void Metadata_ConcurrentDictionary_SuportaMultiplasChaves()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-5",
            WorkflowId = "wf-1"
        };

        exec.Metadata["lastAgentId"] = "agent-boleta";
        exec.Metadata["round"] = "3";

        exec.Metadata.Should().ContainKey("lastAgentId").WhoseValue.Should().Be("agent-boleta");
        exec.Metadata.Should().ContainKey("round").WhoseValue.Should().Be("3");
    }

    [Fact]
    public void ProjectId_PadraoEDefault()
    {
        var exec = new WorkflowExecution
        {
            ExecutionId = "exec-6",
            WorkflowId = "wf-1"
        };

        exec.ProjectId.Should().Be("default");
    }

    [Fact]
    public void ExecutionStep_CriaComPropriedadesObrigatorias()
    {
        var step = new ExecutionStep
        {
            StepId = "step-1",
            AgentId = "agent-boleta",
            AgentName = "Boleta Agent"
        };

        step.Status.Should().Be(WorkflowStatus.Pending);
        step.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        step.CompletedAt.Should().BeNull();
        step.TokensUsed.Should().Be(0);
    }
}
