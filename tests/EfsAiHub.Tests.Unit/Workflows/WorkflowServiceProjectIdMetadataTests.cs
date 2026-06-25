using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Core.Orchestration.Validation;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Workflows;

/// <summary>
/// TriggerAsync deve SEMPRE espelhar a coluna ProjectId em metadata["projectId"],
/// senão a tela de Execuções (analytics) — que filtra client-side por
/// metadata.projectId — esconde execuções standalone/ingestão (cujos handlers não
/// populam o dicionário).
/// </summary>
public sealed class WorkflowServiceProjectIdMetadataTests
{
    [Fact]
    public async Task TriggerAsync_injeta_projectId_no_metadata_quando_o_caller_nao_envia()
    {
        WorkflowExecution? captured = null;
        var (svc, defRepo) = Build("proj-x", e => captured = e);
        defRepo.GetByIdAsync("wf-x", Arg.Any<CancellationToken>()).Returns(Def());

        await svc.TriggerAsync("wf-x", "input", metadata: null);

        captured.Should().NotBeNull();
        captured!.Metadata.Should().ContainKey("projectId");
        captured.Metadata["projectId"].Should().Be("proj-x");
    }

    [Fact]
    public async Task TriggerAsync_preserva_projectId_explicito_do_caller()
    {
        WorkflowExecution? captured = null;
        var (svc, defRepo) = Build("proj-x", e => captured = e);
        defRepo.GetByIdAsync("wf-x", Arg.Any<CancellationToken>()).Returns(Def());

        await svc.TriggerAsync("wf-x", "input",
            metadata: new Dictionary<string, string> { ["projectId"] = "explicito-y" });

        captured!.Metadata["projectId"].Should().Be("explicito-y");
    }

    private static WorkflowDefinition Def() => new()
    {
        Id = "wf-x",
        Name = "wf",
        OrchestrationMode = OrchestrationMode.Sequential,
        ProjectId = "proj-x",
        TenantId = "default",
        Agents = [new WorkflowAgentReference { AgentId = "a1", AgentVersionId = "v1" }],
    };

    private static (WorkflowService svc, IWorkflowDefinitionRepository defRepo) Build(
        string projectId, Action<WorkflowExecution> onCreate)
    {
        var defRepo = Substitute.For<IWorkflowDefinitionRepository>();
        var execRepo = Substitute.For<IWorkflowExecutionRepository>();
        execRepo.When(r => r.CreateAsync(Arg.Any<WorkflowExecution>(), Arg.Any<CancellationToken>()))
            .Do(ci => onCreate(ci.Arg<WorkflowExecution>()));

        var agentDefRepo = Substitute.For<IAgentDefinitionRepository>();
        var executorRegistry = Substitute.For<ICodeExecutorRegistry>();
        var validator = new WorkflowValidator(agentDefRepo);
        var edgeInvariants = new EdgeInvariantsValidator(agentDefRepo, executorRegistry);
        var agentInvariants = new WorkflowAgentInvariantsValidator(agentDefRepo);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        var chatRegistry = Substitute.For<IExecutionSlotRegistry>();
        chatRegistry.TryAcquireSlotAsync().Returns(true);

        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext(projectId, isExplicit: true));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        var logger = Substitute.For<ILogger<WorkflowService>>();

        var svc = new WorkflowService(
            defRepo, execRepo, validator, edgeInvariants, agentInvariants,
            scopeFactory, appLifetime, chatRegistry, projectAccessor, tenantAccessor, logger,
            versionRepo: null, agentVersionRepo: null, crossBus: null, projectRepo: null);

        return (svc, defRepo);
    }
}
