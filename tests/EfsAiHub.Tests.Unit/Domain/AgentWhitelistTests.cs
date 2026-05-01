namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentWhitelistTests
{
    private static AgentDefinition Build(string visibility, IReadOnlyList<string>? allowed = null) => new()
    {
        Id = "agent-x",
        Name = "Test",
        Model = new AgentModelConfig { DeploymentName = "gpt-5.4-mini" },
        ProjectId = "owner-proj",
        Visibility = visibility,
        AllowedProjectIds = allowed,
    };

    [Fact]
    public void Owner_Sempre_PodeReferenciar_IndependenteDeWhitelist()
    {
        var agent = Build("global", allowed: ["other-proj"]);

        agent.CanBeReferencedBy("owner-proj").Should().BeTrue();
    }

    [Fact]
    public void NaoOwner_VisibilityProject_NaoPodeReferenciar()
    {
        var agent = Build("project");

        agent.CanBeReferencedBy("other-proj").Should().BeFalse();
    }

    [Fact]
    public void NaoOwner_Global_SemWhitelist_PodeReferenciar()
    {
        var agent = Build("global", allowed: null);

        agent.CanBeReferencedBy("any-proj").Should().BeTrue();
    }

    [Fact]
    public void NaoOwner_Global_WhitelistVazia_NaoPodeReferenciar()
    {
        var agent = Build("global", allowed: []);

        agent.CanBeReferencedBy("other-proj").Should().BeFalse();
    }

    [Fact]
    public void NaoOwner_Global_NaoEstaNaWhitelist_NaoPodeReferenciar()
    {
        var agent = Build("global", allowed: ["proj-a", "proj-b"]);

        agent.CanBeReferencedBy("proj-c").Should().BeFalse();
    }

    [Fact]
    public void NaoOwner_Global_EstaNaWhitelist_PodeReferenciar()
    {
        var agent = Build("global", allowed: ["proj-a", "proj-b"]);

        agent.CanBeReferencedBy("proj-a").Should().BeTrue();
    }

    [Fact]
    public void EnsureInvariants_WhitelistComVisibilityProject_Throws()
    {
        var agent = Build("project", allowed: ["proj-a"]);

        var act = () => agent.EnsureInvariants();

        act.Should().Throw<EfsAiHub.Core.Abstractions.Exceptions.DomainException>()
            .WithMessage("*AllowedProjectIds*");
    }

    [Fact]
    public void EnsureInvariants_WhitelistComVisibilityGlobal_Passa()
    {
        var agent = Build("global", allowed: ["proj-a"]);

        var act = () => agent.EnsureInvariants();

        act.Should().NotThrow();
    }
}
