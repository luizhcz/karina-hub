using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentVersionEffectiveResolverTests(IntegrationWebApplicationFactory factory)
{
    private IAgentVersionRepository VersionRepo =>
        factory.Services.GetRequiredService<IAgentVersionRepository>();

    private static AgentDefinition BuildDefinition(string id, string instructions = "x") => new()
    {
        Id = id,
        Name = $"Agent {id}",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        Instructions = instructions,
        ProjectId = "default",
    };

    private async Task<AgentVersion> AppendAsync(
        string agentId,
        int revision,
        string instructions,
        bool breakingChange = false,
        string? changeReason = null)
    {
        var def = BuildDefinition(agentId, instructions);
        var version = AgentVersion.FromDefinition(
            def, revision, promptContent: instructions, promptVersionId: null,
            changeReason: changeReason,
            breakingChange: breakingChange);
        return await VersionRepo.AppendAsync(version);
    }

    [Fact]
    public async Task ResolveEffective_PinIgualCurrent_RetornaPin()
    {
        var agentId = $"agent-resolve-eq-{Guid.NewGuid():N}";
        var v1 = await AppendAsync(agentId, 1, "v1");

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v1.AgentVersionId);

        resolved.AgentVersionId.Should().Be(v1.AgentVersionId);
        resolved.Revision.Should().Be(1);
    }

    [Fact]
    public async Task ResolveEffective_PinAncestorSemBreaking_PropagaCurrent()
    {
        var agentId = $"agent-resolve-prop-{Guid.NewGuid():N}";
        var v1 = await AppendAsync(agentId, 1, "v1", breakingChange: false);
        await AppendAsync(agentId, 2, "v2", breakingChange: false, changeReason: "fix typo");
        var v3 = await AppendAsync(agentId, 3, "v3", breakingChange: false, changeReason: "perf tune");

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v1.AgentVersionId);

        // v1 → current (v3) propaga porque não há breaking entre.
        resolved.AgentVersionId.Should().Be(v3.AgentVersionId);
        resolved.Revision.Should().Be(3);
    }

    [Fact]
    public async Task ResolveEffective_PinAncestorComBreakingEntreElEs_RetornaPinExato()
    {
        var agentId = $"agent-resolve-exact-{Guid.NewGuid():N}";
        var v1 = await AppendAsync(agentId, 1, "v1", breakingChange: false);
        await AppendAsync(agentId, 2, "v2-breaking", breakingChange: true, changeReason: "schema mudou");
        await AppendAsync(agentId, 3, "v3", breakingChange: false, changeReason: "post-fix");

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v1.AgentVersionId);

        // v1 → current (v3) NÃO propaga porque v2 é breaking entre eles.
        resolved.AgentVersionId.Should().Be(v1.AgentVersionId);
        resolved.Revision.Should().Be(1);
    }

    [Fact]
    public async Task ResolveEffective_PinIntermediarioPosBreaking_PropagaParaCurrent()
    {
        var agentId = $"agent-resolve-mid-{Guid.NewGuid():N}";
        await AppendAsync(agentId, 1, "v1", breakingChange: false);
        await AppendAsync(agentId, 2, "v2-breaking", breakingChange: true, changeReason: "schema");
        var v3 = await AppendAsync(agentId, 3, "v3", breakingChange: false, changeReason: "patch1");
        var v4 = await AppendAsync(agentId, 4, "v4", breakingChange: false, changeReason: "patch2");

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v3.AgentVersionId);

        // v3 está depois do breaking; v4 é só patch — propaga.
        resolved.AgentVersionId.Should().Be(v4.AgentVersionId);
    }

    [Fact]
    public async Task ResolveEffective_PinIgualOrMaiorQueCurrent_RetornaPin()
    {
        var agentId = $"agent-resolve-future-{Guid.NewGuid():N}";
        var v1 = await AppendAsync(agentId, 1, "v1");

        // Caller pinou em v1, sem mais publishes. Pin >= current.
        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v1.AgentVersionId);
        resolved.AgentVersionId.Should().Be(v1.AgentVersionId);
    }

    [Fact]
    public async Task ResolveEffective_PinInexistente_Lanca()
    {
        var agentId = $"agent-resolve-none-{Guid.NewGuid():N}";
        await AppendAsync(agentId, 1, "v1");

        var act = async () => await VersionRepo.ResolveEffectiveAsync(agentId, "version-que-nao-existe");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pin*não foi encontrada*");
    }

    [Fact]
    public async Task ResolveEffective_PinDeOutroAgent_Lanca()
    {
        var agentA = $"agent-A-{Guid.NewGuid():N}";
        var agentB = $"agent-B-{Guid.NewGuid():N}";
        var vA = await AppendAsync(agentA, 1, "vA");
        await AppendAsync(agentB, 1, "vB");

        var act = async () => await VersionRepo.ResolveEffectiveAsync(agentB, vA.AgentVersionId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*não pertence ao agent*");
    }

    [Fact]
    public async Task GetAncestorBreaking_RetornaPrimeiraNoIntervalo()
    {
        var agentId = $"agent-ancestor-{Guid.NewGuid():N}";
        await AppendAsync(agentId, 1, "v1", breakingChange: false);
        await AppendAsync(agentId, 2, "v2", breakingChange: true, changeReason: "first breaking");
        await AppendAsync(agentId, 3, "v3", breakingChange: true, changeReason: "second breaking");
        await AppendAsync(agentId, 4, "v4", breakingChange: false);

        var ancestor = await VersionRepo.GetAncestorBreakingAsync(agentId, fromRevisionExclusive: 1, toRevisionInclusive: 4);

        ancestor.Should().NotBeNull();
        ancestor!.Revision.Should().Be(2);
        ancestor.ChangeReason.Should().Be("first breaking");
    }

    [Fact]
    public async Task GetAncestorBreaking_SemBreakingNoIntervalo_RetornaNull()
    {
        var agentId = $"agent-ancestor-none-{Guid.NewGuid():N}";
        await AppendAsync(agentId, 1, "v1", breakingChange: false);
        await AppendAsync(agentId, 2, "v2", breakingChange: false, changeReason: "patch");

        var ancestor = await VersionRepo.GetAncestorBreakingAsync(agentId, fromRevisionExclusive: 1, toRevisionInclusive: 2);

        ancestor.Should().BeNull();
    }

    [Fact]
    public async Task GetAncestorBreaking_BreakingForaDoIntervalo_RetornaNull()
    {
        var agentId = $"agent-ancestor-out-{Guid.NewGuid():N}";
        await AppendAsync(agentId, 1, "v1", breakingChange: true, changeReason: "old breaking");
        await AppendAsync(agentId, 2, "v2", breakingChange: false);
        await AppendAsync(agentId, 3, "v3", breakingChange: false, changeReason: "patch");

        // Procura entre rev 2 e 3 — breaking em rev 1 está fora.
        var ancestor = await VersionRepo.GetAncestorBreakingAsync(agentId, fromRevisionExclusive: 2, toRevisionInclusive: 3);

        ancestor.Should().BeNull();
    }

    [Fact]
    public async Task AppendAsync_BreakingTrueSemChangeReason_RejeitaAntesDePersistir()
    {
        var agentId = $"agent-append-invalid-{Guid.NewGuid():N}";
        var def = BuildDefinition(agentId);
        var bad = AgentVersion.FromDefinition(
            def, revision: 1, promptContent: null, promptVersionId: null,
            changeReason: null,
            breakingChange: true);

        var act = async () => await VersionRepo.AppendAsync(bad);

        await act.Should().ThrowAsync<EfsAiHub.Core.Abstractions.Exceptions.DomainException>()
            .WithMessage("*BreakingChange=true exige ChangeReason*");

        // Confirma que NADA foi persistido.
        var versions = await VersionRepo.ListByDefinitionAsync(agentId);
        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_PromotedColumnsRefletemBreakingChangeESchemaVersion()
    {
        var agentId = $"agent-promoted-{Guid.NewGuid():N}";
        var v1 = await AppendAsync(agentId, 1, "v1", breakingChange: true, changeReason: "first");

        var fetched = await VersionRepo.GetByIdAsync(v1.AgentVersionId);

        fetched.Should().NotBeNull();
        fetched!.BreakingChange.Should().BeTrue();
    }
}
