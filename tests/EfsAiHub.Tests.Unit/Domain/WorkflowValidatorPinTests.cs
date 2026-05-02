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
        MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
        OutputSchema: null,
        Resilience: null,
        CostBudget: null,
        SkillRefs: Array.Empty<SkillRef>(),
        ContentHash: "hash",
        Tools: Array.Empty<AgentToolSnapshot>());

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
    public async Task ValidateAsync_SemPin_RejeitaComMensagemDirecionandoListaDeVersions()
    {
        // Pin é obrigatório global pós-cleanup pré-prod (não há mais MandatoryPin flag).
        var (validator, versionRepo) = BuildValidator();
        var def = BuildWorkflow("agent-x");

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("agent-x") && e.Contains("pin de versão"));
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
    public async Task ValidateAsync_SemVersionRepo_AceitaPinSemValidarExistencia()
    {
        // Sem IAgentVersionRepository injetado (cenário de teste isolado), o validator
        // não pode confirmar existência do pin — aceita defensivamente.
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var validator = new WorkflowValidator(agentRepo, versionRepo: null);
        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-qualquer");

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_HandoffComAgentDisabled_RejeitaComMensagemClara()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());
        agentRepo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(new AgentDefinition
            {
                Id = "agent-x",
                Name = "Agent X",
                Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
                Enabled = false,
            });
        agentRepo.GetByIdAsync("agent-y", Arg.Any<CancellationToken>())
            .Returns(new AgentDefinition
            {
                Id = "agent-y",
                Name = "Agent Y",
                Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
                Enabled = true,
            });

        var versionRepo = Substitute.For<IAgentVersionRepository>();
        versionRepo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => BuildVersion(callInfo.Arg<string>(), callInfo.Arg<string>().Replace("v-", "agent-")));

        var validator = new WorkflowValidator(agentRepo, versionRepo);
        var def = new WorkflowDefinition
        {
            Id = "wf-handoff",
            Name = "Handoff",
            OrchestrationMode = OrchestrationMode.Handoff,
            Agents =
            [
                new WorkflowAgentReference { AgentId = "agent-x", AgentVersionId = "v-x" },
                new WorkflowAgentReference { AgentId = "agent-y", AgentVersionId = "v-y" },
            ],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("agent-x") && e.Contains("desabilitado") && e.Contains("Handoff"));
    }

    [Fact]
    public async Task ValidateAsync_SequentialComAgentDisabled_AceitaComoNonBlocking()
    {
        // Sequential tolera skip silencioso em runtime (UI sinaliza via status service).
        // Validator não bloqueia o save.
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        var versionRepo = Substitute.For<IAgentVersionRepository>();
        versionRepo.GetByIdAsync("v-1", Arg.Any<CancellationToken>())
            .Returns(BuildVersion("v-1", "agent-x"));

        var validator = new WorkflowValidator(agentRepo, versionRepo);
        var def = BuildWorkflow("agent-x", pinnedVersionId: "v-1");

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
    }
}
