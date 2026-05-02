using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentVersionBreakingValidationTests
{
    private static AgentVersion BuildValid(
        string? changeReason = null,
        bool breakingChange = false) =>
        new(
            AgentVersionId: "v-1",
            AgentDefinitionId: "agent-1",
            Revision: 1,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: "user-1",
            ChangeReason: changeReason,
            Status: AgentVersionStatus.Published,
            PromptContent: "instr",
            PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4o", null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, false),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: Array.Empty<SkillRef>(),
            ContentHash: "deadbeef",
            Tools: Array.Empty<AgentToolSnapshot>(),
            BreakingChange: breakingChange);

    [Fact]
    public void EnsureInvariants_BreakingTrueComChangeReason_Passa()
    {
        var v = BuildValid(changeReason: "Mudança de schema do output", breakingChange: true);

        var act = () => v.EnsureInvariants();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_BreakingTrueSemChangeReason_Lanca()
    {
        var v = BuildValid(changeReason: null, breakingChange: true);

        var act = () => v.EnsureInvariants();

        act.Should().Throw<DomainException>()
            .WithMessage("*BreakingChange=true exige ChangeReason*");
    }

    [Fact]
    public void EnsureInvariants_BreakingTrueComChangeReasonVazia_Lanca()
    {
        var v = BuildValid(changeReason: "   ", breakingChange: true);

        var act = () => v.EnsureInvariants();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EnsureInvariants_BreakingFalseSemChangeReason_Passa()
    {
        // Patch (BreakingChange=false, default) não exige reason — fix de typo, etc.
        var v = BuildValid(changeReason: null, breakingChange: false);

        var act = () => v.EnsureInvariants();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_AgentVersionIdVazio_Lanca()
    {
        var v = BuildValid() with { AgentVersionId = "" };

        var act = () => v.EnsureInvariants();

        act.Should().Throw<DomainException>().WithMessage("*AgentVersionId*");
    }

    [Fact]
    public void EnsureInvariants_AgentDefinitionIdVazio_Lanca()
    {
        var v = BuildValid() with { AgentDefinitionId = "" };

        var act = () => v.EnsureInvariants();

        act.Should().Throw<DomainException>().WithMessage("*AgentDefinitionId*");
    }

    [Fact]
    public void EnsureInvariants_ContentHashVazio_Lanca()
    {
        var v = BuildValid() with { ContentHash = "" };

        var act = () => v.EnsureInvariants();

        act.Should().Throw<DomainException>().WithMessage("*ContentHash*");
    }
}
