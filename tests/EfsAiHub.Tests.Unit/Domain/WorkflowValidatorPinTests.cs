using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Host.Api.Services;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class WorkflowValidatorPinTests
{
    private static (WorkflowValidator validator, IAgentVersionRepository versionRepo) BuildValidator()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var versionRepo = Substitute.For<IAgentVersionRepository>();
        return (new WorkflowValidator(agentRepo, versionRepo), versionRepo);
    }

    private static AgentVersion BuildVersion(string versionId, string agentDefId) => new(
        AgentVersionId: versionId,
        AgentDefinitionId: agentDefId,
        Revision: 1,
        CreatedAt: DateTime.UtcNow,
        CreatedBy: null,
        ChangeReason: null,
        Status: AgentVersionStatus.Published,
        PromptContent: null,
        PromptVersionId: null,
        Model: new AgentModelSnapshot("gpt-4o", null, null),
        Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, false),
        ToolFingerprints: Array.Empty<ToolFingerprint>(),
        MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
        OutputSchema: null,
        Resilience: null,
        CostBudget: null,
        SkillRefs: Array.Empty<SkillRef>(),
        ContentHash: "hash");

    private static WorkflowDefinition BuildWorkflow(string agentId, string? pinnedVersionId = null) => new()
    {
        Id = "wf-pin-test",
        Name = "Workflow Pin Test",
        OrchestrationMode = OrchestrationMode.Sequential,
        Agents =
        [
            new WorkflowAgentReference
            {
                AgentId = agentId,
                AgentVersionId = pinnedVersionId,
            },
        ],
    };

    [Fact]
    public async Task ValidateAsync_SemPin_NaoValidaPinExistencia()
    {
        var (validator, versionRepo) = BuildValidator();
        var def = BuildWorkflow("agent-x");

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
        await versionRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    [Fact]
    public async Task ValidateAsync_PinExistenteEPertenceAoAgent_Aceita()
    {
        var (validator, versionRepo) = BuildValidator();
        versionRepo.GetByIdAsync("v-1", Arg.Any<CancellationToken>())
            .Returns(BuildVersion("v-1", "agent-x"));

        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-1");
        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_PinInexistente_RejeitaComMensagemClara()
    {
        var (validator, versionRepo) = BuildValidator();
        versionRepo.GetByIdAsync("v-fantasma", Arg.Any<CancellationToken>())
            .Returns((AgentVersion?)null);

        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-fantasma");
        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("v-fantasma") && e.Contains("não foi encontrada"));
    }

    [Fact]
    public async Task ValidateAsync_PinDeOutroAgent_Rejeita()
    {
        var (validator, versionRepo) = BuildValidator();
        versionRepo.GetByIdAsync("v-1", Arg.Any<CancellationToken>())
            .Returns(BuildVersion("v-1", "agent-OUTRO"));

        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-1");
        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("v-1") && e.Contains("não pertence ao agent"));
    }

    [Fact]
    public async Task ValidateAsync_SemVersionRepo_NaoBloqueia()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        // versionRepo=null preserva BC com chamadores legacy.
        var validator = new WorkflowValidator(agentRepo, versionRepo: null);
        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-qualquer");

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
    }
}
