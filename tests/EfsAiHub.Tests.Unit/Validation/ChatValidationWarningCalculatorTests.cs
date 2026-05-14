using EfsAiHub.Core.Orchestration.Validation;

namespace EfsAiHub.Tests.Unit.Validation;

[Trait("Category", "Unit")]
public class ChatValidationWarningCalculatorTests
{
    private static AgentDefinition StubAgent(
        string id, AgentType type, string? validatedAgentVersionId = null) => new()
    {
        Id = id,
        Name = id,
        Type = type,
        Enabled = true,
        Model = new AgentModelConfig { DeploymentName = "gpt-4" },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI", ClientType = "ChatCompletion" },
        ProjectId = "sales-trader-ai",
        TenantId = "default",
        Visibility = "project",
        LastChatSandboxValidatedAt = validatedAgentVersionId is null ? null : DateTime.UtcNow,
        LastChatSandboxValidatedByUserId = validatedAgentVersionId is null ? null : "admin",
        LastChatSandboxValidatedAgentVersionId = validatedAgentVersionId,
    };

    private static AgentVersion StubVersion(string versionId, string agentId, int revision) =>
        new(
            AgentVersionId: versionId,
            AgentDefinitionId: agentId,
            Revision: revision,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: null, ChangeReason: null,
            Status: AgentVersionStatus.Published,
            PromptContent: null, PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4", null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, true),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null, Resilience: null, CostBudget: null,
            SkillRefs: Array.Empty<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: "h", Description: null, Metadata: null,
            FallbackProvider: null, Tools: null, BreakingChange: false);

    private static (ChatValidationWarningCalculator calc,
                    IAgentDefinitionRepository agentRepo,
                    IAgentVersionRepository versionRepo)
        Build(params AgentDefinition[] agents)
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        foreach (var a in agents)
        {
            agentRepo.GetByIdAsync(a.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AgentDefinition?>(a));
        }
        var versionRepo = Substitute.For<IAgentVersionRepository>();
        return (new ChatValidationWarningCalculator(agentRepo, versionRepo), agentRepo, versionRepo);
    }

    private static WorkflowDefinition ChatDeploy(params WorkflowAgentReference[] agents) => new()
    {
        Id = "deploy-chat-x",
        Name = "Chat deploy",
        OrchestrationMode = OrchestrationMode.Graph,
        ProjectId = "sales-trader-ai",
        TenantId = "default",
        Configuration = new WorkflowConfiguration { InputMode = "Chat" },
        Metadata = new Dictionary<string, string> { ["deploymentKind"] = "chat" },
        Agents = agents.ToList(),
    };

    [Fact]
    public async Task ComputeAsync_NonChatDeploy_ReturnsEmpty()
    {
        var (calc, _, _) = Build(StubAgent("conv-1", AgentType.Conversational));
        var def = new WorkflowDefinition
        {
            Id = "wf-batch", Name = "Pipeline",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "p", TenantId = "t",
            Configuration = new WorkflowConfiguration { InputMode = "Standalone" },
            Agents = [new WorkflowAgentReference { AgentId = "conv-1", AgentVersionId = "v-1" }],
        };

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAsync_ChatSandboxKind_ReturnsEmpty()
    {
        var (calc, _, _) = Build(StubAgent("conv-1", AgentType.Conversational));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "conv-1", AgentVersionId = "v-1", Role = "EntryPoint",
        });
        ((Dictionary<string, string>)def.Metadata)["kind"] = "chat-sandbox";

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAsync_RouterAgent_Ignored()
    {
        var (calc, _, _) = Build(StubAgent("router-1", AgentType.Conversational));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "router-1", AgentVersionId = "v-1", Role = "Router",
        });

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAsync_BranchAgentWithoutValidation_EmitsNoChatSandboxValidation()
    {
        var (calc, _, versionRepo) = Build(StubAgent("conv-1", AgentType.Conversational));
        versionRepo.GetByIdAsync("v-pin-3", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("v-pin-3", "conv-1", 3)));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "conv-1", AgentVersionId = "v-pin-3", Role = "BranchAgent",
        });

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().HaveCount(1);
        warnings[0].Reason.Should().Be(ChatValidationReasons.NoChatSandboxValidation);
        warnings[0].AgentId.Should().Be("conv-1");
        warnings[0].PinnedAgentVersionId.Should().Be("v-pin-3");
        warnings[0].PinnedRevision.Should().Be(3);
        warnings[0].ValidatedAgentVersionId.Should().BeNull();
    }

    [Fact]
    public async Task ComputeAsync_BranchAgentValidationMismatch_EmitsValidationStale()
    {
        var (calc, _, versionRepo) = Build(StubAgent("conv-1", AgentType.Conversational, validatedAgentVersionId: "v-validated-1"));
        versionRepo.GetByIdAsync("v-pin-3", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("v-pin-3", "conv-1", 3)));
        versionRepo.GetByIdAsync("v-validated-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("v-validated-1", "conv-1", 1)));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "conv-1", AgentVersionId = "v-pin-3", Role = "BranchAgent",
        });

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().HaveCount(1);
        warnings[0].Reason.Should().Be(ChatValidationReasons.ValidationStale);
        warnings[0].PinnedRevision.Should().Be(3);
        warnings[0].ValidatedAgentVersionId.Should().Be("v-validated-1");
        warnings[0].ValidatedRevision.Should().Be(1);
    }

    [Fact]
    public async Task ComputeAsync_BranchAgentValidationMatchesPin_NoWarning()
    {
        var (calc, _, versionRepo) = Build(StubAgent("conv-1", AgentType.Conversational, validatedAgentVersionId: "v-pin-3"));
        versionRepo.GetByIdAsync("v-pin-3", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("v-pin-3", "conv-1", 3)));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "conv-1", AgentVersionId = "v-pin-3", Role = "BranchAgent",
        });

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeAsync_NonConversationalBranch_Ignored()
    {
        var (calc, _, _) = Build(StubAgent("worker-1", AgentType.Worker));
        var def = ChatDeploy(new WorkflowAgentReference
        {
            AgentId = "worker-1", AgentVersionId = "v-pin-1", Role = "BranchAgent",
        });

        var warnings = await calc.ComputeAsync(def);
        warnings.Should().BeEmpty();
    }
}
