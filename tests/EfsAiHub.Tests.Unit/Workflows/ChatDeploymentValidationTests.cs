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

[Trait("Category", "Unit")]
public class ChatDeploymentValidationTests
{
    private const string TenantId = "default";
    private const string AllowedProjectId = "sales-trader-ai";
    private const string BlockedProjectId = "default";

    private static (WorkflowService service, IProjectRepository projectRepo, IWorkflowDefinitionRepository defRepo)
        BuildService(Project? projectOnLookup)
    {
        var defRepo = Substitute.For<IWorkflowDefinitionRepository>();
        defRepo.UpsertAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<WorkflowDefinition>()));

        var execRepo = Substitute.For<IWorkflowExecutionRepository>();

        var agentDefRepo = Substitute.For<IAgentDefinitionRepository>();
        agentDefRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlySet<string>)call.Arg<IEnumerable<string>>().ToHashSet());

        var executorRegistry = Substitute.For<ICodeExecutorRegistry>();

        var validator = new WorkflowValidator(agentDefRepo);
        var edgeInvariants = new EdgeInvariantsValidator(agentDefRepo, executorRegistry);
        var agentInvariants = new WorkflowAgentInvariantsValidator(agentDefRepo);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var appLifetime = Substitute.For<IHostApplicationLifetime>();
        var chatRegistry = Substitute.For<IExecutionSlotRegistry>();

        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext(
            projectOnLookup?.Id ?? BlockedProjectId, isExplicit: true));

        var tenantAccessor = Substitute.For<ITenantContextAccessor>();

        var projectRepo = Substitute.For<IProjectRepository>();
        projectRepo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(projectOnLookup));

        var logger = Substitute.For<ILogger<WorkflowService>>();

        var service = new WorkflowService(
            defRepo,
            execRepo,
            validator,
            edgeInvariants,
            agentInvariants,
            scopeFactory,
            appLifetime,
            chatRegistry,
            projectAccessor,
            tenantAccessor,
            logger,
            versionRepo: null,
            agentVersionRepo: null,
            crossBus: null,
            projectRepo: projectRepo);

        return (service, projectRepo, defRepo);
    }

    private static WorkflowDefinition BuildChatDeployment(string projectId) => new()
    {
        Id = "deploy-chat-test",
        Name = "Chat deploy test",
        OrchestrationMode = OrchestrationMode.Graph,
        ProjectId = projectId,
        TenantId = TenantId,
        Configuration = new WorkflowConfiguration { InputMode = "Chat" },
        Metadata = new Dictionary<string, string> { ["deploymentKind"] = "chat" },
        Agents =
        [
            new WorkflowAgentReference { AgentId = "router-1", AgentVersionId = "v-1", Role = "Router" },
            new WorkflowAgentReference { AgentId = "conv-1", AgentVersionId = "v-1", Role = "BranchAgent" },
        ],
    };

    private static WorkflowDefinition BuildSimpleSequential(string projectId) => new()
    {
        Id = "wf-simple",
        Name = "Workflow não-chat",
        OrchestrationMode = OrchestrationMode.Sequential,
        ProjectId = projectId,
        TenantId = TenantId,
        Agents = [new WorkflowAgentReference { AgentId = "agent-1", AgentVersionId = "v-1" }],
    };

    // Espelha BuildChatDeployment, mas marca kind=chat-sandbox + transient=true
    // (igual ao que o AgentSandboxService grava). É o caminho do bug original:
    // workflow efêmero criado pelo botão "Testar" no AgentEditor.
    private static WorkflowDefinition BuildChatSandboxDeployment(string projectId) => new()
    {
        Id = $"deploy-chat-sandbox-{Guid.NewGuid():N}",
        Name = "Chat sandbox test",
        OrchestrationMode = OrchestrationMode.Graph,
        ProjectId = projectId,
        TenantId = TenantId,
        Configuration = new WorkflowConfiguration { InputMode = "Chat" },
        Metadata = new Dictionary<string, string>
        {
            [AgentSandboxMetadata.DeploymentKindKey] = AgentSandboxMetadata.DeploymentKindChat,
            [AgentSandboxMetadata.KindKey] = AgentSandboxMetadata.KindChatSandbox,
            [AgentSandboxMetadata.TransientKey] = "true",
            [AgentSandboxMetadata.SessionIdKey] = Guid.NewGuid().ToString("N"),
        },
        Agents =
        [
            new WorkflowAgentReference { AgentId = "conv-1", AgentVersionId = "v-1", Role = "EntryPoint" },
        ],
    };

    [Fact]
    public async Task CreateAsync_ChatDeployment_RejectsWhenProjectNotAllowed()
    {
        var project = new Project
        {
            Id = BlockedProjectId,
            Name = "Default",
            TenantId = TenantId,
            ChatDeploymentAllowed = false,
        };
        var (service, projectRepo, defRepo) = BuildService(project);

        var def = BuildChatDeployment(BlockedProjectId);

        var act = async () => await service.CreateAsync(def);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Contain("chat_deployment_allowed=true");
        ex.Which.Message.Should().Contain(BlockedProjectId);

        await projectRepo.Received(1).GetByIdAsync(BlockedProjectId, Arg.Any<CancellationToken>());
        await defRepo.DidNotReceive().UpsertAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ChatDeployment_AcceptsWhenProjectAllowed()
    {
        var project = new Project
        {
            Id = AllowedProjectId,
            Name = "Sales Trader AI",
            TenantId = TenantId,
            ChatDeploymentAllowed = true,
        };
        var (service, projectRepo, defRepo) = BuildService(project);

        var def = BuildChatDeployment(AllowedProjectId);

        // Gate passa; pode falhar downstream em invariantes de Graph/Edge mas NÃO
        // com UnauthorizedAccessException. O critério do teste é: o gate de chat
        // não rejeita projeto autorizado.
        var caught = await Record.ExceptionAsync(() => service.CreateAsync(def));

        (caught as UnauthorizedAccessException).Should().BeNull();
        await projectRepo.Received(1).GetByIdAsync(AllowedProjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NonChatDeployment_IgnoresProjectFlag()
    {
        // Mesmo projeto bloqueado pra chat: deploy não-chat passa pelo gate sem
        // consultar IProjectRepository.
        var project = new Project
        {
            Id = BlockedProjectId,
            Name = "Default",
            TenantId = TenantId,
            ChatDeploymentAllowed = false,
        };
        var (service, projectRepo, _) = BuildService(project);

        var def = BuildSimpleSequential(BlockedProjectId);

        var caught = await Record.ExceptionAsync(() => service.CreateAsync(def));

        (caught as UnauthorizedAccessException).Should().BeNull();
        await projectRepo.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ChatSandboxWorkflow_BypassesGate_WhenProjectNotAllowed()
    {
        // Cenário do bug original: usuário tenta testar um Conversational em
        // projeto sem ChatDeploymentAllowed. Sandbox tem kind=chat-sandbox e
        // por isso bypassa o gate — não é deploy de produção.
        var project = new Project
        {
            Id = BlockedProjectId,
            Name = "Default",
            TenantId = TenantId,
            ChatDeploymentAllowed = false,
        };
        var (service, projectRepo, _) = BuildService(project);

        var def = BuildChatSandboxDeployment(BlockedProjectId);

        var caught = await Record.ExceptionAsync(() => service.CreateAsync(def));

        (caught as UnauthorizedAccessException).Should().BeNull();
        // Sandbox passa antes de chegar no _projectRepo.GetByIdAsync — confirma
        // que o gate sequer foi consultado pra deploy não-produtivo.
        await projectRepo.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ChatSandboxWorkflow_BypassesGate_WhenProjectAllowed()
    {
        // Sanity check: sandbox passa direto independente do flag do projeto.
        var project = new Project
        {
            Id = AllowedProjectId,
            Name = "Sales Trader AI",
            TenantId = TenantId,
            ChatDeploymentAllowed = true,
        };
        var (service, projectRepo, _) = BuildService(project);

        var def = BuildChatSandboxDeployment(AllowedProjectId);

        var caught = await Record.ExceptionAsync(() => service.CreateAsync(def));

        (caught as UnauthorizedAccessException).Should().BeNull();
        await projectRepo.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RemovingChatSandboxMarker_ReengagesGate()
    {
        // Regression: admin que tira metadata.kind=chat-sandbox via PUT em projeto
        // não autorizado deve receber 401 — o gate reativa porque o workflow
        // deixa de ser sandbox.
        var project = new Project
        {
            Id = BlockedProjectId,
            Name = "Default",
            TenantId = TenantId,
            ChatDeploymentAllowed = false,
        };
        var (service, projectRepo, defRepo) = BuildService(project);

        var existing = BuildChatSandboxDeployment(BlockedProjectId);
        // Simula workflow sandbox já publicado.
        defRepo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkflowDefinition?>(existing));

        // Update: mantém deploymentKind=chat mas REMOVE kind=chat-sandbox —
        // simula promoção fora do AgentSandbox. Sem o marcador, vira deploy
        // produtivo e o gate reativa.
        var promoted = new WorkflowDefinition
        {
            Id = existing.Id,
            Name = existing.Name,
            OrchestrationMode = OrchestrationMode.Graph,
            ProjectId = BlockedProjectId,
            TenantId = TenantId,
            Configuration = new WorkflowConfiguration { InputMode = "Chat" },
            Metadata = new Dictionary<string, string>
            {
                [AgentSandboxMetadata.DeploymentKindKey] = AgentSandboxMetadata.DeploymentKindChat,
            },
            Agents =
            [
                new WorkflowAgentReference { AgentId = "conv-1", AgentVersionId = "v-1", Role = "EntryPoint" },
            ],
        };

        var act = async () => await service.UpdateAsync(promoted);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Contain("chat_deployment_allowed=true");

        await projectRepo.Received(1).GetByIdAsync(BlockedProjectId, Arg.Any<CancellationToken>());
    }
}
