using EfsAiHub.Core.Orchestration.Validation;

namespace EfsAiHub.Tests.Unit.Workflows;

[Trait("Category", "Unit")]
public class WorkflowAgentInvariantsValidatorTests
{
    private static AgentDefinition StubAgent(string id, AgentType type) => new()
    {
        Id = id,
        Name = id,
        Type = type,
        Model = new AgentModelConfig { DeploymentName = "gpt-4" },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI", ClientType = "ChatCompletion" },
        ProjectId = "default",
        TenantId = "default",
        Visibility = "project",
        Enabled = true,
    };

    private static (WorkflowAgentInvariantsValidator validator, IAgentDefinitionRepository repo) Build(
        params AgentDefinition[] agents)
    {
        var repo = Substitute.For<IAgentDefinitionRepository>();
        foreach (var agent in agents)
        {
            repo.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AgentDefinition?>(agent));
        }
        return (new WorkflowAgentInvariantsValidator(repo), repo);
    }

    private static WorkflowDefinition Workflow(string inputMode, params (string id, string? versionId)[] agentRefs) => new()
    {
        Id = "wf-test",
        Name = "Test",
        OrchestrationMode = OrchestrationMode.Graph,
        ProjectId = "default",
        TenantId = "default",
        Configuration = new WorkflowConfiguration { InputMode = inputMode },
        Agents = agentRefs
            .Select(r => new WorkflowAgentReference { AgentId = r.id, AgentVersionId = r.versionId })
            .ToList(),
    };

    [Fact]
    public async Task ValidateAsync_StandaloneWithConversational_ReturnsError()
    {
        var (validator, _) = Build(StubAgent("conv-1", AgentType.Conversational));

        var errors = await validator.ValidateAsync(Workflow("Standalone", ("conv-1", "v-1")));

        errors.Should().HaveCount(1);
        errors[0].ErrorCode.Should().Be(WorkflowErrorCodes.ConversationalRequiresChat);
        errors[0].Message.Should().Contain("conv-1");
        errors[0].Hint.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_ChatWithConversational_NoError()
    {
        var (validator, _) = Build(StubAgent("conv-1", AgentType.Conversational));

        var errors = await validator.ValidateAsync(Workflow("Chat", ("conv-1", "v-1")));

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_StandaloneWithCustom_NoError()
    {
        var (validator, _) = Build(StubAgent("custom-1", AgentType.Custom));

        var errors = await validator.ValidateAsync(Workflow("Standalone", ("custom-1", "v-1")));

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_StandaloneMixedAgents_OnlyConversationalErrors()
    {
        var (validator, _) = Build(
            StubAgent("custom-1", AgentType.Custom),
            StubAgent("conv-1", AgentType.Conversational),
            StubAgent("worker-1", AgentType.Worker),
            StubAgent("conv-2", AgentType.Conversational));

        var errors = await validator.ValidateAsync(
            Workflow("Standalone",
                ("custom-1", "v-1"),
                ("conv-1", "v-1"),
                ("worker-1", "v-1"),
                ("conv-2", "v-1")));

        errors.Should().HaveCount(2);
        errors.Should().AllSatisfy(e => e.ErrorCode.Should().Be(WorkflowErrorCodes.ConversationalRequiresChat));
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("conv-1"));
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("conv-2"));
    }

    [Fact]
    public async Task ValidateAsync_EmptyAgents_NoError()
    {
        var (validator, _) = Build();

        var errors = await validator.ValidateAsync(Workflow("Standalone"));

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_AgentNotFound_SkipsSilently()
    {
        var repo = Substitute.For<IAgentDefinitionRepository>();
        repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentDefinition?>(null));
        var validator = new WorkflowAgentInvariantsValidator(repo);

        var errors = await validator.ValidateAsync(Workflow("Standalone", ("missing", "v-1")));

        // Agente inexistente é responsabilidade do WorkflowValidator.ValidateAgentReferencesAsync.
        // Aqui apenas pulamos pra não emitir erro duplicado.
        errors.Should().BeEmpty();
    }
}
