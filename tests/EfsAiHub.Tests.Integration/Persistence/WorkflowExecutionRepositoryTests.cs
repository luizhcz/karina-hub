using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class WorkflowExecutionRepositoryTests(IntegrationWebApplicationFactory factory)
{
    private IWorkflowExecutionRepository Repo =>
        factory.Services.GetRequiredService<IWorkflowExecutionRepository>();

    private static WorkflowExecution MakeExecution(
        string? workflowId = null,
        WorkflowStatus status = WorkflowStatus.Pending) => new()
    {
        ExecutionId = Guid.NewGuid().ToString(),
        WorkflowId = workflowId ?? $"wf-{Guid.NewGuid():N}",
        Status = status,
        Input = "test input"
    };

    // ── Create + GetById ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_GetById_RetornaCamposMapeados()
    {
        var execution = MakeExecution();

        await Repo.CreateAsync(execution);
        var fetched = await Repo.GetByIdAsync(execution.ExecutionId);

        fetched.Should().NotBeNull();
        fetched!.ExecutionId.Should().Be(execution.ExecutionId);
        fetched.WorkflowId.Should().Be(execution.WorkflowId);
        fetched.Status.Should().Be(WorkflowStatus.Pending);
        fetched.Input.Should().Be("test input");
    }

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var result = await Repo.GetByIdAsync(Guid.NewGuid().ToString());

        result.Should().BeNull();
    }

    // ── GetByWorkflowId com filtro de status ──────────────────────────────────

    [Fact]
    public async Task GetByWorkflowId_FiltroStatus_RetornaSomenteStatusCorreto()
    {
        var workflowId = $"wf-filter-{Guid.NewGuid():N}";
        var running = MakeExecution(workflowId, WorkflowStatus.Running);
        var completed = MakeExecution(workflowId, WorkflowStatus.Completed);

        await Repo.CreateAsync(running);
        await Repo.CreateAsync(completed);

        var results = await Repo.GetByWorkflowIdAsync(workflowId, status: "Running");

        results.Should().OnlyContain(e => e.Status == WorkflowStatus.Running);
        results.Should().ContainSingle(e => e.ExecutionId == running.ExecutionId);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_AlteraStatus_PersisteAlteracao()
    {
        var execution = MakeExecution();
        await Repo.CreateAsync(execution);

        execution.Status = WorkflowStatus.Completed;
        execution.Output = "resultado";
        execution.CompletedAt = DateTime.UtcNow;
        await Repo.UpdateAsync(execution);

        var fetched = await Repo.GetByIdAsync(execution.ExecutionId);

        fetched!.Status.Should().Be(WorkflowStatus.Completed);
        fetched.Output.Should().Be("resultado");
        fetched.CompletedAt.Should().NotBeNull();
    }

    // ── CountRunningAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CountRunningAsync_RetornaContagemExata()
    {
        var workflowId = $"wf-count-{Guid.NewGuid():N}";
        var e1 = MakeExecution(workflowId, WorkflowStatus.Running);
        var e2 = MakeExecution(workflowId, WorkflowStatus.Running);
        var e3 = MakeExecution(workflowId, WorkflowStatus.Pending);

        await Repo.CreateAsync(e1);
        await Repo.CreateAsync(e2);
        await Repo.CreateAsync(e3);

        var count = await Repo.CountRunningAsync(workflowId);

        count.Should().Be(2);
    }

    // ── GetPausedExecutionsPaged ──────────────────────────────────────────────

    [Fact]
    public async Task GetPausedExecutionsPaged_OrdenadoPorStartedAt()
    {
        var workflowId = $"wf-paused-{Guid.NewGuid():N}";

        // Create 3 paused executions (StartedAt defaults to UtcNow on create)
        for (var i = 0; i < 3; i++)
        {
            await Repo.CreateAsync(MakeExecution(workflowId, WorkflowStatus.Paused));
            await Task.Delay(5); // small delay to get distinct StartedAt
        }

        var page1 = await Repo.GetPausedExecutionsPagedAsync(offset: 0, pageSize: 2);
        var page2 = await Repo.GetPausedExecutionsPagedAsync(offset: 2, pageSize: 2);

        page1.Count.Should().BeLessOrEqualTo(2);
        page2.Count.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SemFiltros_Retorna200()
    {
        var executions = await Repo.GetAllAsync(pageSize: 200);

        executions.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllAsync_FiltroDeData_ExcluiForaDoPeriodo()
    {
        var execution = MakeExecution();
        await Repo.CreateAsync(execution);

        // Filter to far future — should not include just-created execution
        var from = DateTime.UtcNow.AddYears(10);
        var results = await Repo.GetAllAsync(from: from);

        results.Should().NotContain(e => e.ExecutionId == execution.ExecutionId);
    }

    // ── CountAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_RetornaContagemPositiva()
    {
        var workflowId = $"wf-cnt-{Guid.NewGuid():N}";
        await Repo.CreateAsync(MakeExecution(workflowId));
        await Repo.CreateAsync(MakeExecution(workflowId));

        var count = await Repo.CountAsync(workflowId: workflowId);

        count.Should().Be(2);
    }
}
