using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.PredefinedModels;
using EfsAiHub.Platform.Runtime.Tools.PredefinedModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class PredefinedModelBinderTests
{
    private static (PredefinedModelBinder binder, IPredefinedModelRepository repo) Build()
    {
        var repo = Substitute.For<IPredefinedModelRepository>();
        var binder = new PredefinedModelBinder(repo, NullLogger<PredefinedModelBinder>.Instance);
        return (binder, repo);
    }

    private static AgentDefinition AgentWithPreset(string? presetId) => new()
    {
        Id = "agent-x",
        Name = "X",
        Model = new AgentModelConfig
        {
            DeploymentName = presetId is null ? "gpt-3.5" : "",
            Temperature = 0.7f,
            MaxTokens = 1000,
            PredefinedModelId = presetId,
        },
        Provider = new AgentProviderConfig
        {
            Type = "OpenAI",
            ApiKey = "sk-original",
        },
        ProjectId = "p",
        TenantId = "t",
    };

    private static PredefinedModel ValidPreset(bool enabled = true) => new()
    {
        Id = "smart-default",
        DisplayName = "Smart",
        Provider = "AzureOpenAI",
        ClientType = "ChatCompletion",
        DeploymentName = "gpt-4o",
        DefaultTemperature = 0.3f,
        DefaultMaxTokens = 4096,
        Enabled = enabled,
    };

    [Fact]
    public async Task BindAsync_AgentSemPresetId_RetornaInchanged()
    {
        var (binder, repo) = Build();
        var def = AgentWithPreset(null);

        var result = await binder.BindAsync(def);

        result.Should().BeSameAs(def);
        await repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    [Fact]
    public async Task BindAsync_PresetNaoExiste_LogaEretornaUnchanged()
    {
        var (binder, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns((PredefinedModel?)null);

        var def = AgentWithPreset("smart-default");
        var result = await binder.BindAsync(def);

        result.Should().BeSameAs(def);
    }

    [Fact]
    public async Task BindAsync_PresetDisabled_LogaEretornaUnchanged()
    {
        var (binder, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(ValidPreset(enabled: false));

        var def = AgentWithPreset("smart-default");
        var result = await binder.BindAsync(def);

        result.Should().BeSameAs(def);
    }

    [Fact]
    public async Task BindAsync_PresetOk_HidrataModelEProvider()
    {
        var (binder, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(ValidPreset());

        var def = AgentWithPreset("smart-default");
        var result = await binder.BindAsync(def);

        result.Should().NotBeSameAs(def);
        result.Model.DeploymentName.Should().Be("gpt-4o");
        result.Model.Temperature.Should().Be(0.3f);
        result.Model.MaxTokens.Should().Be(4096);
        result.Model.PredefinedModelId.Should().Be("smart-default");
        result.Provider.Type.Should().Be("AzureOpenAI");
        result.Provider.ClientType.Should().Be("ChatCompletion");
    }

    [Fact]
    public async Task BindAsync_PreservaApiKeyDoAgent()
    {
        var (binder, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(ValidPreset());

        var def = AgentWithPreset("smart-default");
        var result = await binder.BindAsync(def);

        result.Provider.ApiKey.Should().Be("sk-original");
    }

    [Fact]
    public async Task BindAsync_PresetSemTemperatureDefault_PreservaTemperatureDoAgent()
    {
        var (binder, repo) = Build();
        var preset = ValidPreset();
        preset.DefaultTemperature = null;
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(preset);

        var def = AgentWithPreset("smart-default");
        var result = await binder.BindAsync(def);

        result.Model.Temperature.Should().Be(0.7f);
    }
}
