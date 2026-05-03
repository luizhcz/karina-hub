using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.PredefinedModels;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class PredefinedModelInvariantsTests
{
    private static PredefinedModel ValidPreset() => new()
    {
        Id = "smart-default",
        DisplayName = "Smart",
        Description = "Modelo balanceado pra uso geral.",
        Provider = "AzureOpenAI",
        ClientType = "ChatCompletion",
        DeploymentName = "gpt-4o",
        DefaultTemperature = 0.3f,
        DefaultMaxTokens = 4096,
        Enabled = true,
    };

    [Fact]
    public void EnsureInvariants_PresetCompleto_NaoLanca()
    {
        var preset = ValidPreset();
        Action act = preset.EnsureInvariants;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_IdVazio_LancaDomainException()
    {
        var preset = new PredefinedModel
        {
            Id = "",
            DisplayName = "X",
            Provider = "AzureOpenAI",
            DeploymentName = "gpt-4o",
        };
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*Id*");
    }

    [Fact]
    public void EnsureInvariants_DisplayNameVazio_LancaDomainException()
    {
        var preset = ValidPreset();
        preset.DisplayName = "";
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*DisplayName*");
    }

    [Fact]
    public void EnsureInvariants_ProviderVazio_LancaDomainException()
    {
        var preset = ValidPreset();
        preset.Provider = "";
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*Provider*");
    }

    [Fact]
    public void EnsureInvariants_DeploymentNameVazio_LancaDomainException()
    {
        var preset = ValidPreset();
        preset.DeploymentName = "";
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*DeploymentName*");
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.5f)]
    [InlineData(3f)]
    public void EnsureInvariants_TemperatureForaDoIntervalo_LancaDomainException(float temperature)
    {
        var preset = ValidPreset();
        preset.DefaultTemperature = temperature;
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*Temperature*");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void EnsureInvariants_TemperatureNoLimite_NaoLanca(float temperature)
    {
        var preset = ValidPreset();
        preset.DefaultTemperature = temperature;
        Action act = preset.EnsureInvariants;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_TemperatureNull_NaoLanca()
    {
        var preset = ValidPreset();
        preset.DefaultTemperature = null;
        Action act = preset.EnsureInvariants;
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void EnsureInvariants_MaxTokensNaoPositivo_LancaDomainException(int maxTokens)
    {
        var preset = ValidPreset();
        preset.DefaultMaxTokens = maxTokens;
        Action act = preset.EnsureInvariants;
        act.Should().Throw<DomainException>().WithMessage("*MaxTokens*");
    }

    [Fact]
    public void EnsureInvariants_MaxTokensNull_NaoLanca()
    {
        var preset = ValidPreset();
        preset.DefaultMaxTokens = null;
        Action act = preset.EnsureInvariants;
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_DescriptionVazia_NaoLanca()
    {
        var preset = ValidPreset();
        preset.Description = "";
        Action act = preset.EnsureInvariants;
        act.Should().NotThrow();
    }
}
