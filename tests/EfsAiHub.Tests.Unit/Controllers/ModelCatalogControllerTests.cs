using EfsAiHub.Host.Api.Controllers;
using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.AspNetCore.Mvc;

namespace EfsAiHub.Tests.Unit.Controllers;

public class ModelCatalogControllerTests
{
    private readonly IModelCatalogRepository _repo = Substitute.For<IModelCatalogRepository>();
    private readonly ModelCatalogController _sut;

    public ModelCatalogControllerTests()
    {
        _sut = new ModelCatalogController(_repo);
    }

    // ── List ──────────────────────────────────────────────────

    [Fact]
    public async Task List_Returns200_WithModels()
    {
        var models = new List<ModelCatalog>
        {
            new() { Id = "gpt-4o", Provider = "OPENAI", DisplayName = "GPT-4o" }
        };
        _repo.GetAllAsync(null, true, 1, 50, Arg.Any<CancellationToken>()).Returns(models);
        _repo.CountAsync(null, true, Arg.Any<CancellationToken>()).Returns(models.Count);

        var result = await _sut.List(null, true, 1, 50, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task List_WithProvider_PassesFilter()
    {
        _repo.GetAllAsync("OPENAI", true, 1, 50, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ModelCatalog>());
        _repo.CountAsync("OPENAI", true, Arg.Any<CancellationToken>()).Returns(0);

        await _sut.List("OPENAI", true, 1, 50, CancellationToken.None);

        await _repo.Received(1).GetAllAsync("OPENAI", true, 1, 50, Arg.Any<CancellationToken>());
    }

    // ── GetById ───────────────────────────────────────────────

    [Fact]
    public async Task GetById_Exists_Returns200()
    {
        var model = new ModelCatalog { Id = "gpt-4o", Provider = "OPENAI", DisplayName = "GPT-4o" };
        _repo.GetByIdAsync("gpt-4o", "OPENAI", Arg.Any<CancellationToken>()).Returns(model);

        var result = await _sut.GetById("OPENAI", "gpt-4o", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        _repo.GetByIdAsync("missing", "OPENAI", Arg.Any<CancellationToken>())
            .Returns((ModelCatalog?)null);

        var result = await _sut.GetById("OPENAI", "missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── Upsert ────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_MissingId_Returns400()
    {
        var request = new UpsertModelCatalogRequest("", "OPENAI", "Name", null, null, null, null);

        var result = await _sut.Upsert(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upsert_MissingProvider_Returns400()
    {
        var request = new UpsertModelCatalogRequest("gpt-4o", "", "Name", null, null, null, null);

        var result = await _sut.Upsert(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upsert_Valid_Returns200_AndNormalizesProvider()
    {
        var savedModel = new ModelCatalog { Id = "gpt-4o", Provider = "OPENAI", DisplayName = "GPT-4o" };
        _repo.UpsertAsync(Arg.Any<ModelCatalog>(), Arg.Any<CancellationToken>()).Returns(savedModel);

        var request = new UpsertModelCatalogRequest("gpt-4o", "openai", "GPT-4o", null, null, null, null);
        var result = await _sut.Upsert(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received(1).UpsertAsync(
            Arg.Is<ModelCatalog>(m => m.Provider == "OPENAI"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upsert_NullCapabilities_DefaultsToEmptyList()
    {
        var savedModel = new ModelCatalog { Id = "gpt-4o", Provider = "OPENAI", DisplayName = "GPT-4o" };
        _repo.UpsertAsync(Arg.Any<ModelCatalog>(), Arg.Any<CancellationToken>()).Returns(savedModel);

        var request = new UpsertModelCatalogRequest("gpt-4o", "OPENAI", "GPT-4o", null, null, null, null);
        await _sut.Upsert(request, CancellationToken.None);

        await _repo.Received(1).UpsertAsync(
            Arg.Is<ModelCatalog>(m => m.Capabilities.Count == 0),
            Arg.Any<CancellationToken>());
    }

    // ── Deactivate ────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_Found_Returns204()
    {
        _repo.SetActiveAsync("gpt-4o", "OPENAI", false, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Deactivate("OPENAI", "gpt-4o", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Deactivate_NotFound_Returns404()
    {
        _repo.SetActiveAsync("missing", "OPENAI", false, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Deactivate("OPENAI", "missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
