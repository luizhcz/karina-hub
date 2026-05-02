using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class PatchPropagationTests(IntegrationWebApplicationFactory factory)
{
    private IAgentVersionRepository VersionRepo =>
        factory.Services.GetRequiredService<IAgentVersionRepository>();

    private static AgentDefinition Build(string id, string instructions) => new()
    {
        Id = id,
        Name = $"Agent {id}",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        Instructions = instructions,
        ProjectId = "default",
    };

    private async Task<AgentVersion> AppendVersionAsync(
        string agentId,
        int revision,
        string instructions,
        bool breakingChange,
        string? changeReason = null)
    {
        var def = Build(agentId, instructions);
        var version = AgentVersion.FromDefinition(
            def, revision,
            promptContent: instructions,
            promptVersionId: null,
            changeReason: changeReason,
            breakingChange: breakingChange);
        return await VersionRepo.AppendAsync(version);
    }

    /// <summary>
    /// Cenário matrix com 5 versions onde v4 é breaking:
    /// v1 (patch) → v2 (patch) → v3 (patch) → v4 (BREAKING) → v5 (patch)
    /// </summary>
    private async Task<(string agentId, AgentVersion v1, AgentVersion v2, AgentVersion v3, AgentVersion v4, AgentVersion v5)>
        SetupFiveVersionMatrixAsync()
    {
        var agentId = $"agent-matrix-{Guid.NewGuid():N}";
        var v1 = await AppendVersionAsync(agentId, 1, "v1", breakingChange: false);
        var v2 = await AppendVersionAsync(agentId, 2, "v2", breakingChange: false, changeReason: "patch fix");
        var v3 = await AppendVersionAsync(agentId, 3, "v3", breakingChange: false, changeReason: "perf tune");
        var v4 = await AppendVersionAsync(agentId, 4, "v4", breakingChange: true, changeReason: "schema change");
        var v5 = await AppendVersionAsync(agentId, 5, "v5", breakingChange: false, changeReason: "post-fix");
        return (agentId, v1, v2, v3, v4, v5);
    }

    [Fact]
    public async Task PinV1_PropagaAteV3_BloqueadaPorV4Breaking()
    {
        var (agentId, v1, _, v3, _, _) = await SetupFiveVersionMatrixAsync();

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v1.AgentVersionId);

        // Pin v1 → propaga até v3 (próximo é v4 breaking → para no pinado, retorna snapshot v1).
        // O algoritmo retorna o pin original quando há breaking entre pinned e current,
        // pois o caller deve ficar preso ao snapshot estável.
        resolved.AgentVersionId.Should().Be(v1.AgentVersionId);
        resolved.Revision.Should().Be(1);
    }

    [Fact]
    public async Task PinV2_TemBreakingV4EntreElasECurrentV5_RetornaSnapshotV2()
    {
        var (agentId, _, v2, _, _, _) = await SetupFiveVersionMatrixAsync();

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v2.AgentVersionId);

        // v2 → v5 atravessa v4 breaking → retorna pin exato (v2).
        resolved.AgentVersionId.Should().Be(v2.AgentVersionId);
    }

    [Fact]
    public async Task PinV3_BreakingV4EntreElasECurrentV5_RetornaSnapshotV3()
    {
        var (agentId, _, _, v3, _, _) = await SetupFiveVersionMatrixAsync();

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v3.AgentVersionId);

        // v3 → v5 atravessa v4 breaking → fica em v3.
        resolved.AgentVersionId.Should().Be(v3.AgentVersionId);
    }

    [Fact]
    public async Task PinV4_PropagaParaCurrentV5_SemBreakingEntreElas()
    {
        var (agentId, _, _, _, v4, v5) = await SetupFiveVersionMatrixAsync();

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v4.AgentVersionId);

        // v4 (próprio breaking) → v5 (patch). Range exclusivo (4, 5] tem só v5 (patch),
        // não há breaking ENTRE v4 e v5 → propaga.
        resolved.AgentVersionId.Should().Be(v5.AgentVersionId);
    }

    [Fact]
    public async Task PinV5_IgualCurrent_RetornaCurrent()
    {
        var (agentId, _, _, _, _, v5) = await SetupFiveVersionMatrixAsync();

        var resolved = await VersionRepo.ResolveEffectiveAsync(agentId, v5.AgentVersionId);

        resolved.AgentVersionId.Should().Be(v5.AgentVersionId);
    }

    [Fact]
    public async Task GetAncestorBreaking_NoIntervaloV1V5_RetornaV4()
    {
        var (agentId, _, _, _, v4, _) = await SetupFiveVersionMatrixAsync();

        var ancestor = await VersionRepo.GetAncestorBreakingAsync(
            agentId, fromRevisionExclusive: 1, toRevisionInclusive: 5);

        ancestor.Should().NotBeNull();
        ancestor!.Revision.Should().Be(4);
        ancestor.AgentVersionId.Should().Be(v4.AgentVersionId);
        ancestor.ChangeReason.Should().Be("schema change");
    }

    [Fact]
    public async Task GetAncestorBreaking_NoIntervaloV4V5_RetornaNull()
    {
        var (agentId, _, _, _, _, _) = await SetupFiveVersionMatrixAsync();

        // Range (4, 5] — exclui v4 (breaking) — só v5 (patch) sobra.
        var ancestor = await VersionRepo.GetAncestorBreakingAsync(
            agentId, fromRevisionExclusive: 4, toRevisionInclusive: 5);

        ancestor.Should().BeNull();
    }

    [Fact]
    public async Task GetAncestorBreaking_NoIntervaloV1V3_RetornaNull()
    {
        var (agentId, _, _, _, _, _) = await SetupFiveVersionMatrixAsync();

        // Range (1, 3] — só v2/v3 (patches) — sem breaking.
        var ancestor = await VersionRepo.GetAncestorBreakingAsync(
            agentId, fromRevisionExclusive: 1, toRevisionInclusive: 3);

        ancestor.Should().BeNull();
    }
}
