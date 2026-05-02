namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentEnabledTests
{
    private static AgentDefinition Build(bool enabled = true, string visibility = "project") => new()
    {
        Id = "a-1",
        Name = "Agent",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        ProjectId = "owner-project",
        Visibility = visibility,
        Enabled = enabled,
    };

    [Fact]
    public void Enabled_DefaultTrue()
    {
        var def = new AgentDefinition
        {
            Id = "a-default",
            Name = "Agent",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };

        def.Enabled.Should().BeTrue();
    }

    [Fact]
    public void CanBeInvokedBy_AgentEnabled_OwnerProject_True()
    {
        var def = Build(enabled: true);

        def.CanBeInvokedBy("owner-project").Should().BeTrue();
    }

    [Fact]
    public void CanBeInvokedBy_AgentDisabled_OwnerProject_False()
    {
        var def = Build(enabled: false);

        def.CanBeInvokedBy("owner-project").Should().BeFalse();
    }

    [Fact]
    public void CanBeInvokedBy_GlobalEnabled_OutroProject_True()
    {
        var def = Build(enabled: true, visibility: "global");

        def.CanBeInvokedBy("caller-project").Should().BeTrue();
    }

    [Fact]
    public void CanBeInvokedBy_GlobalDisabled_OutroProject_False()
    {
        // Disabled curto-circuita Visibility/Whitelist — short-circuit em Enabled.
        var def = Build(enabled: false, visibility: "global");

        def.CanBeInvokedBy("caller-project").Should().BeFalse();
    }
}
