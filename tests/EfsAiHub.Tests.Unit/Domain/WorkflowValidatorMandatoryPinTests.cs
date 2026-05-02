using EfsAiHub.Core.Abstractions.Sharing;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class WorkflowValidatorMandatoryPinTests
{
    private static (WorkflowValidator validator, IAgentVersionRepository versionRepo) BuildValidator(
        bool mandatoryPin)
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var versionRepo = Substitute.For<IAgentVersionRepository>();

        var monitor = Substitute.For<IOptionsMonitor<SharingOptions>>();
        monitor.CurrentValue.Returns(new SharingOptions { MandatoryPin = mandatoryPin });

        var validator = new WorkflowValidator(agentRepo, versionRepo, monitor);
        return (validator, versionRepo);
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
        Id = "wf-mandatory-pin",
        Name = "Workflow Mandatory Pin",
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
    public async Task MandatoryPinOn_RefSemPin_RejeitaComMensagemClara()
    {
        var (validator, _) = BuildValidator(mandatoryPin: true);
        var def = BuildWorkflow("agent-x", pinnedVersionId: null);

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e =>
            e.Contains("agent-x") &&
            e.Contains("MandatoryPin=true") &&
            e.Contains("/api/agents/agent-x/versions"));
    }

    [Fact]
    public async Task MandatoryPinOff_RefSemPin_Aceita()
    {
        var (validator, _) = BuildValidator(mandatoryPin: false);
        var def = BuildWorkflow("agent-x", pinnedVersionId: null);

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MandatoryPinOn_PinExistenteEPertenceAoAgent_Aceita()
    {
        var (validator, versionRepo) = BuildValidator(mandatoryPin: true);
        versionRepo.GetByIdAsync("v-1", Arg.Any<CancellationToken>())
            .Returns(BuildVersion("v-1", "agent-x"));

        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-1");
        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MandatoryPinOn_PinInexistente_RejeitaComMensagemClara()
    {
        var (validator, versionRepo) = BuildValidator(mandatoryPin: true);
        versionRepo.GetByIdAsync("v-fantasma", Arg.Any<CancellationToken>())
            .Returns((AgentVersion?)null);

        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-fantasma");
        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("v-fantasma") && e.Contains("não foi encontrada"));
        // Pin é não-vazio: bypass do check de MandatoryPin, vai direto pro check de existência.
        errors.Should().NotContain(e => e.Contains("MandatoryPin=true"));
    }

    [Fact]
    public async Task SemSharingOptions_BCDefaultMandatoryPinFalse_AceitaSemPin()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        // sharingOptions=null preserva BC com chamadores legacy.
        var validator = new WorkflowValidator(agentRepo, versionRepo: null, sharingOptions: null);
        var def = BuildWorkflow("agent-x", pinnedVersionId: null);

        var (isValid, _) = await validator.ValidateAsync(def);
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task MandatoryPinOn_MultipleRefs_ListaTodosOsErros()
    {
        var (validator, _) = BuildValidator(mandatoryPin: true);
        var def = new WorkflowDefinition
        {
            Id = "wf-multi",
            Name = "Workflow Multi",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents =
            [
                new WorkflowAgentReference { AgentId = "agent-a", AgentVersionId = null },
                new WorkflowAgentReference { AgentId = "agent-b", AgentVersionId = null },
            ],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Count(e => e.Contains("MandatoryPin=true")).Should().Be(2);
    }

    [Fact]
    public async Task TenantStaged_MandatoryPinOnComWhitelist_TenantMatchEnforce()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var monitor = Substitute.For<IOptionsMonitor<EfsAiHub.Core.Abstractions.Sharing.SharingOptions>>();
        monitor.CurrentValue.Returns(new EfsAiHub.Core.Abstractions.Sharing.SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "tenant-A" },
        });

        var validator = new WorkflowValidator(agentRepo, versionRepo: null, sharingOptions: monitor);
        var def = BuildWorkflow("agent-x", pinnedVersionId: null);
        def.TenantId = "tenant-A"; // tenant na whitelist → enforce

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("MandatoryPin=true"));
    }

    [Fact]
    public async Task TenantStaged_MandatoryPinOnComWhitelist_TenantForaNaoEnforce()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var monitor = Substitute.For<IOptionsMonitor<EfsAiHub.Core.Abstractions.Sharing.SharingOptions>>();
        monitor.CurrentValue.Returns(new EfsAiHub.Core.Abstractions.Sharing.SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "tenant-A" },
        });

        var validator = new WorkflowValidator(agentRepo, versionRepo: null, sharingOptions: monitor);
        var def = BuildWorkflow("agent-x", pinnedVersionId: null);
        def.TenantId = "tenant-B"; // tenant fora da whitelist → não enforce

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
    }
}
