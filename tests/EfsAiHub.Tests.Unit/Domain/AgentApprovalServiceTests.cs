using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentApprovalServiceTests
{
    private static (AgentApprovalService svc, IAgentDraftRepository draftRepo) Build()
    {
        var draftRepo = Substitute.For<IAgentDraftRepository>();
        var svc = new AgentApprovalService(draftRepo, Substitute.For<ILogger<AgentApprovalService>>());
        return (svc, draftRepo);
    }

    [Fact]
    public async Task ListPendingAsync_DelegaProRepoComStatusPendingApproval()
    {
        var (svc, draftRepo) = Build();
        draftRepo.ListByStatusForTenantAsync(AgentDraftStatus.PendingApproval, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AgentDraft>());

        await svc.ListPendingAsync();

        await draftRepo.Received(1)
            .ListByStatusForTenantAsync(AgentDraftStatus.PendingApproval, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_PassaActorUserIdEChangeReasonProRepo()
    {
        var (svc, draftRepo) = Build();
        var publishedAgent = new AgentDefinition
        {
            Id = "agent-x",
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };
        draftRepo.ApproveAsync("d-1", "actor-y", "razão",
                Arg.Any<AgentApprovalAction>(), Arg.Any<AgentChangeTier?>(), Arg.Any<CancellationToken>())
            .Returns(publishedAgent);

        var result = await svc.ApproveAsync("d-1", "actor-y", "razão");

        result.Should().Be(publishedAgent);
        await draftRepo.Received(1).ApproveAsync("d-1", "actor-y", "razão",
            Arg.Any<AgentApprovalAction>(), Arg.Any<AgentChangeTier?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_PassaFeedbackProRepo()
    {
        var (svc, draftRepo) = Build();
        var rejectedDraft = new AgentDraft
        {
            Id = "d-1",
            Name = "x",
            Payload = new AgentDraftPayload(),
            ProjectId = "p",
            TenantId = "t",
            Status = AgentDraftStatus.Rejected,
            RejectionFeedback = "muito longo",
        };
        draftRepo.RejectAsync("d-1", "actor-y", "muito longo", Arg.Any<CancellationToken>())
            .Returns(rejectedDraft);

        var result = await svc.RejectAsync("d-1", "actor-y", "muito longo");

        result.Status.Should().Be(AgentDraftStatus.Rejected);
        result.RejectionFeedback.Should().Be("muito longo");
    }

    [Fact]
    public async Task ApproveAsync_PropagaDraftRePublishRaceException()
    {
        var (svc, draftRepo) = Build();
        draftRepo.ApproveAsync("d-1", "actor", null,
                Arg.Any<AgentApprovalAction>(), Arg.Any<AgentChangeTier?>(), Arg.Any<CancellationToken>())
            .Returns<AgentDefinition>(_ => throw new DraftRePublishRaceException("d-1", 3, 5));

        Func<Task> act = () => svc.ApproveAsync("d-1", "actor", null);

        await act.Should().ThrowAsync<DraftRePublishRaceException>()
            .Where(e => e.BaseRevision == 3 && e.CurrentRevision == 5);
    }

    [Fact]
    public async Task ApproveAsync_PropagaDraftStatusTransitionException()
    {
        var (svc, draftRepo) = Build();
        draftRepo.ApproveAsync("d-1", "actor", null,
                Arg.Any<AgentApprovalAction>(), Arg.Any<AgentChangeTier?>(), Arg.Any<CancellationToken>())
            .Returns<AgentDefinition>(_ =>
                throw new DraftStatusTransitionException("d-1", AgentDraftStatus.Draft, "approve"));

        Func<Task> act = () => svc.ApproveAsync("d-1", "actor", null);

        await act.Should().ThrowAsync<DraftStatusTransitionException>()
            .Where(e => e.CurrentStatus == AgentDraftStatus.Draft && e.Operation == "approve");
    }

    [Fact]
    public async Task RejectAsync_FeedbackVazioPropagaArgumentException()
    {
        var (svc, draftRepo) = Build();
        draftRepo.RejectAsync("d-1", "actor", "", Arg.Any<CancellationToken>())
            .Returns<AgentDraft>(_ => throw new ArgumentException("Feedback obrigatório."));

        Func<Task> act = () => svc.RejectAsync("d-1", "actor", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
