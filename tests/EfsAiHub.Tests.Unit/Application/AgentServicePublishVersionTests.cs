using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents.Skills;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServicePublishVersionTests
{
    private static (AgentService service, IAgentDefinitionRepository repo,
        IAgentVersionRepository versionRepo, IProjectContextAccessor accessor) Build(
        string callerProjectId = "alpha")
    {
        var repo = Substitute.For<IAgentDefinitionRepository>();
        var promptRepo = Substitute.For<IAgentPromptRepository>();
        var versionRepo = Substitute.For<IAgentVersionRepository>();
        var accessor = Substitute.For<IProjectContextAccessor>();
        accessor.Current.Returns(new ProjectContext(callerProjectId));

        var service = new AgentService(
            repository: repo,
            promptRepo: promptRepo,
            projectAccessor: accessor,
            logger: Substitute.For<ILogger<AgentService>>(),
            versionRepo: versionRepo);

        return (service, repo, versionRepo, accessor);
    }

    private static AgentDefinition BuildAgent(string id = "agent-x", string projectId = "alpha") => new()
    {
        Id = id,
        Name = "Agent Test",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        Instructions = "x",
        ProjectId = projectId,
    };

    [Fact]
    public async Task PublishVersion_CallerProjectIdDifereDoOwner_LancaUnauthorizedAccess()
    {
        var (service, repo, _, _) = Build(callerProjectId: "beta");
        repo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(BuildAgent(projectId: "alpha")); // owner = alpha; caller = beta

        var act = async () => await service.PublishVersionAsync(
            "agent-x", breakingChange: false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*apenas o projeto dono*");
    }

    [Fact]
    public async Task PublishVersion_AgentInexistente_LancaKeyNotFound()
    {
        var (service, repo, _, _) = Build();
        repo.GetByIdAsync("agent-fantasma", Arg.Any<CancellationToken>())
            .Returns((AgentDefinition?)null);

        var act = async () => await service.PublishVersionAsync(
            "agent-fantasma", breakingChange: false);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PublishVersion_SemVersionRepo_LancaInvalidOperation()
    {
        var repo = Substitute.For<IAgentDefinitionRepository>();
        var promptRepo = Substitute.For<IAgentPromptRepository>();
        var accessor = Substitute.For<IProjectContextAccessor>();
        accessor.Current.Returns(new ProjectContext("alpha"));

        // versionRepo=null preserva BC com legacy callers.
        var service = new AgentService(repo, promptRepo, accessor,
            Substitute.For<ILogger<AgentService>>(), versionRepo: null);

        var act = async () => await service.PublishVersionAsync(
            "agent-x", breakingChange: false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requer IAgentVersionRepository*");
    }

    [Fact]
    public async Task PublishVersion_OwnerEhCaller_PropagaParaAppendAsync()
    {
        var (service, repo, versionRepo, _) = Build(callerProjectId: "alpha");
        repo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(BuildAgent(projectId: "alpha"));
        versionRepo.GetNextRevisionAsync("agent-x", Arg.Any<CancellationToken>()).Returns(3);
        // AppendAsync retorna a version persistida (echo).
        versionRepo.AppendAsync(Arg.Any<AgentVersion>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<AgentVersion>());

        var result = await service.PublishVersionAsync(
            "agent-x", breakingChange: true, changeReason: "schema mudou", createdBy: "user-1");

        result.AgentDefinitionId.Should().Be("agent-x");
        result.Revision.Should().Be(3);
        result.BreakingChange.Should().BeTrue();
        result.ChangeReason.Should().Be("schema mudou");
        result.CreatedBy.Should().Be("user-1");
        await versionRepo.Received(1).AppendAsync(Arg.Any<AgentVersion>(), Arg.Any<CancellationToken>());
    }
}
