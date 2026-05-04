using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentInvariantsPredefinedModelTests
{
    [Fact]
    public void EnsureInvariants_ComPredefinedModelId_AceitaDeploymentNameVazio()
    {
        var def = new AgentDefinition
        {
            Id = "x",
            Name = "X",
            Model = new AgentModelConfig
            {
                DeploymentName = "",
                PredefinedModelId = "smart-default",
            },
            ProjectId = "p",
            TenantId = "t",
        };

        Action act = def.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_SemPredefinedModelId_ExigeDeploymentName()
    {
        var def = new AgentDefinition
        {
            Id = "x",
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "" },
            ProjectId = "p",
            TenantId = "t",
        };

        Action act = def.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*DeploymentName*");
    }

    [Fact]
    public void EnsureInvariants_DeploymentNameEPredefinedModelIdAmbosSetados_NaoLanca()
    {
        var def = new AgentDefinition
        {
            Id = "x",
            Name = "X",
            Model = new AgentModelConfig
            {
                DeploymentName = "gpt-4o",
                PredefinedModelId = "smart-default",
            },
            ProjectId = "p",
            TenantId = "t",
        };

        Action act = def.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_TemperatureForaDoIntervalo_LancaMesmoComPreset()
    {
        var def = new AgentDefinition
        {
            Id = "x",
            Name = "X",
            Model = new AgentModelConfig
            {
                DeploymentName = "",
                Temperature = 5f,
                PredefinedModelId = "smart-default",
            },
            ProjectId = "p",
            TenantId = "t",
        };

        Action act = def.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*Temperature*");
    }
}
