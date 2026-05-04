using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.PredefinedModels;
using EfsAiHub.Platform.Runtime.Services;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class PredefinedModelServiceTests
{
    private static (PredefinedModelService svc, IPredefinedModelRepository repo) Build()
    {
        var repo = Substitute.For<IPredefinedModelRepository>();
        var svc = new PredefinedModelService(repo, Substitute.For<ILogger<PredefinedModelService>>());
        return (svc, repo);
    }

    private static PredefinedModel ValidPreset(string id = "smart-default") => new()
    {
        Id = id,
        DisplayName = "Smart",
        Description = "Modelo balanceado.",
        Provider = "AzureOpenAI",
        DeploymentName = "gpt-4o",
        DefaultTemperature = 0.3f,
        DefaultMaxTokens = 4096,
        Enabled = true,
    };

    [Fact]
    public async Task CreateAsync_PresetValido_Persiste()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns((PredefinedModel?)null);
        repo.CreateAsync(Arg.Any<PredefinedModel>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<PredefinedModel>());

        var result = await svc.CreateAsync(ValidPreset());

        result.Id.Should().Be("smart-default");
        await repo.Received(1).CreateAsync(Arg.Any<PredefinedModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_IdJaExiste_LancaIdConflict()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(ValidPreset());

        Func<Task> act = () => svc.CreateAsync(ValidPreset());

        await act.Should().ThrowAsync<PredefinedModelIdConflictException>().WithMessage("*Já existe*");
    }

    [Fact]
    public async Task CreateAsync_TemperatureInvalida_LancaDomain400()
    {
        var (svc, _) = Build();
        var preset = ValidPreset();
        preset.DefaultTemperature = 5f;

        Func<Task> act = () => svc.CreateAsync(preset);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Temperature*");
    }

    [Fact]
    public async Task CreateAsync_TrimaCamposBrancos()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PredefinedModel?)null);
        PredefinedModel? captured = null;
        repo.CreateAsync(Arg.Do<PredefinedModel>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<PredefinedModel>());

        var preset = ValidPreset();
        preset.DisplayName = "  Smart  ";
        preset.Provider = "  AzureOpenAI ";
        preset.DeploymentName = " gpt-4o ";

        await svc.CreateAsync(preset);

        captured.Should().NotBeNull();
        captured!.DisplayName.Should().Be("Smart");
        captured.Provider.Should().Be("AzureOpenAI");
        captured.DeploymentName.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task UpdateAsync_PresetNaoExiste_Lanca404()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((PredefinedModel?)null);

        Func<Task> act = () => svc.UpdateAsync("missing", ValidPreset(), DateTime.UtcNow);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_PreservaCreatedAt_DoExisting()
    {
        var (svc, repo) = Build();
        var existing = ValidPreset();
        var existingCreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var withCreatedAt = new PredefinedModel
        {
            Id = existing.Id,
            DisplayName = existing.DisplayName,
            Provider = existing.Provider,
            DeploymentName = existing.DeploymentName,
            CreatedAt = existingCreatedAt,
        };
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(withCreatedAt);

        PredefinedModel? captured = null;
        repo.UpdateAsync(Arg.Do<PredefinedModel>(p => captured = p), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<PredefinedModel>());

        var patch = ValidPreset();
        patch.DisplayName = "Smart Updated";

        await svc.UpdateAsync("smart-default", patch, withCreatedAt.UpdatedAt);

        captured.Should().NotBeNull();
        captured!.CreatedAt.Should().Be(existingCreatedAt);
        captured.DisplayName.Should().Be("Smart Updated");
    }

    [Fact]
    public async Task DeleteAsync_PresetNaoExiste_Lanca404()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((PredefinedModel?)null);

        Func<Task> act = () => svc.DeleteAsync("missing");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_Sucesso_ChamaRepo()
    {
        var (svc, repo) = Build();
        repo.GetByIdAsync("smart-default", Arg.Any<CancellationToken>()).Returns(ValidPreset());
        repo.DeleteAsync("smart-default", Arg.Any<CancellationToken>()).Returns(true);

        await svc.DeleteAsync("smart-default");

        await repo.Received(1).DeleteAsync("smart-default", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_DefaultIncludeDisabledFalse()
    {
        var (svc, repo) = Build();
        repo.ListAsync(false, Arg.Any<CancellationToken>()).Returns(new List<PredefinedModel> { ValidPreset() });

        await svc.ListAsync();

        await repo.Received(1).ListAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_IncludeDisabledTrue_RepassaFlag()
    {
        var (svc, repo) = Build();
        repo.ListAsync(true, Arg.Any<CancellationToken>()).Returns(new List<PredefinedModel>());

        await svc.ListAsync(includeDisabled: true);

        await repo.Received(1).ListAsync(true, Arg.Any<CancellationToken>());
    }
}
