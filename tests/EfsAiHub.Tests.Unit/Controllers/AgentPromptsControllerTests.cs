using EfsAiHub.Host.Api.Controllers;
using EfsAiHub.Host.Api.Models.Requests;
using Microsoft.AspNetCore.Mvc;

namespace EfsAiHub.Tests.Unit.Controllers;

public class AgentPromptsControllerTests
{
    private readonly IAgentPromptRepository _promptRepo = Substitute.For<IAgentPromptRepository>();
    private readonly IAgentDefinitionRepository _agentRepo = Substitute.For<IAgentDefinitionRepository>();
    private readonly AgentPromptsController _sut;

    public AgentPromptsControllerTests()
    {
        _sut = new AgentPromptsController(_promptRepo, _agentRepo);
    }

    // ── ListVersions ──────────────────────────────────────────

    [Fact]
    public async Task ListVersions_AgentNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("missing", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ListVersions("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListVersions_AgentExists_Returns200()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.ListVersionsAsync("agent-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentPromptVersion>());

        var result = await _sut.ListVersions("agent-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── GetActive ─────────────────────────────────────────────

    [Fact]
    public async Task GetActive_AgentNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("missing", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetActive("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetActive_NoActivePrompt_Returns404()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.GetActivePromptAsync("agent-1", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.GetActive("agent-1", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetActive_HasActivePrompt_Returns200()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.GetActivePromptAsync("agent-1", Arg.Any<CancellationToken>())
            .Returns("You are a helpful assistant.");

        var result = await _sut.GetActive("agent-1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── SaveVersion ───────────────────────────────────────────

    [Fact]
    public async Task SaveVersion_AgentNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("missing", Arg.Any<CancellationToken>()).Returns(false);
        var request = new SavePromptVersionRequest("v1", "content");

        var result = await _sut.SaveVersion("missing", request, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SaveVersion_EmptyVersionId_Returns400()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        var request = new SavePromptVersionRequest("", "content");

        var result = await _sut.SaveVersion("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SaveVersion_EmptyContent_Returns400()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        var request = new SavePromptVersionRequest("v1", "");

        var result = await _sut.SaveVersion("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SaveVersion_Valid_Returns201()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        var request = new SavePromptVersionRequest("v1", "You are helpful.");

        var result = await _sut.SaveVersion("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task SaveVersion_RepoThrowsArgumentException_Returns400()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.SaveVersionAsync("agent-1", "v1", "content", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ArgumentException("duplicate")));
        var request = new SavePromptVersionRequest("v1", "content");

        var result = await _sut.SaveVersion("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── SetMaster ─────────────────────────────────────────────

    [Fact]
    public async Task SetMaster_AgentNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("missing", Arg.Any<CancellationToken>()).Returns(false);
        var request = new SetMasterRequest("v1");

        var result = await _sut.SetMaster("missing", request, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SetMaster_EmptyVersionId_Returns400()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        var request = new SetMasterRequest("");

        var result = await _sut.SetMaster("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetMaster_VersionNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.SetMasterAsync("agent-1", "v99", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new KeyNotFoundException("not found")));
        var request = new SetMasterRequest("v99");

        var result = await _sut.SetMaster("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SetMaster_Valid_Returns200()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        var request = new SetMasterRequest("v1");

        var result = await _sut.SetMaster("agent-1", request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── ClearMaster ───────────────────────────────────────────

    [Fact]
    public async Task ClearMaster_AgentNotFound_Returns404()
    {
        _agentRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((AgentDefinition?)null);

        var result = await _sut.ClearMaster("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ClearMaster_Valid_RestoresOriginalAndReturns204()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-1",
            Name = "Test",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Instructions = "Você é um assistente útil."
        };
        _agentRepo.GetByIdAsync("agent-1", Arg.Any<CancellationToken>()).Returns(agent);

        var result = await _sut.ClearMaster("agent-1", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await _promptRepo.Received(1).RestoreOriginalAsync(
            "agent-1", "Você é um assistente útil.", Arg.Any<CancellationToken>());
    }

    // ── DeleteVersion ─────────────────────────────────────────

    [Fact]
    public async Task DeleteVersion_AgentNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("missing", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteVersion("missing", "v1", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteVersion_IsActiveVersion_Returns409()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.DeleteVersionAsync("agent-1", "v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cannot delete active")));

        var result = await _sut.DeleteVersion("agent-1", "v1", CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DeleteVersion_VersionNotFound_Returns404()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);
        _promptRepo.DeleteVersionAsync("agent-1", "v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new KeyNotFoundException("not found")));

        var result = await _sut.DeleteVersion("agent-1", "v1", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteVersion_Valid_Returns204()
    {
        _agentRepo.ExistsAsync("agent-1", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteVersion("agent-1", "v1", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }
}
