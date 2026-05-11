using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentDraftServiceTests
{
    private static (AgentDraftService svc,
        IAgentDraftRepository draftRepo,
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository versionRepo) Build(string projectId = "owner", string tenantId = "tenant-a")
    {
        var draftRepo = Substitute.For<IAgentDraftRepository>();
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        var versionRepo = Substitute.For<IAgentVersionRepository>();

        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext(projectId));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.Current.Returns(new TenantContext(tenantId));

        var svc = new AgentDraftService(
            draftRepo, agentRepo, versionRepo,
            projectAccessor, tenantAccessor,
            Substitute.For<ILogger<AgentDraftService>>());

        return (svc, draftRepo, agentRepo, versionRepo);
    }

    [Fact]
    public async Task CreateAsync_ColisaoComAgentPublicado_RejeitaCom409()
    {
        var (svc, draftRepo, agentRepo, _) = Build();
        agentRepo.ExistsAsync("agent-x", Arg.Any<CancellationToken>()).Returns(true);
        draftRepo.ExistsAsync("agent-x", Arg.Any<CancellationToken>()).Returns(false);

        Func<Task> act = () => svc.CreateAsync("agent-x", new AgentDraftPayload());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*já é usado por agent publicado*");
    }

    [Fact]
    public async Task CreateAsync_ColisaoComOutroDraft_RejeitaCom409()
    {
        var (svc, draftRepo, agentRepo, _) = Build();
        draftRepo.ExistsAsync("agent-x", Arg.Any<CancellationToken>()).Returns(true);

        Func<Task> act = () => svc.CreateAsync("agent-x", new AgentDraftPayload());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Já existe draft*");
    }

    [Fact]
    public async Task CreateEditDraftAsync_AgentDeOutroProjeto_Lanca403()
    {
        var (svc, _, agentRepo, _) = Build(projectId: "p-caller");
        agentRepo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(new AgentDefinition
            {
                Id = "agent-x",
                Name = "X",
                Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
                ProjectId = "p-owner",
                Visibility = "global",
            });

        Func<Task> act = () => svc.CreateEditDraftAsync("agent-x");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*projeto dono*");
    }

    [Fact]
    public async Task CreateEditDraftAsync_AgentNaoExiste_Lanca404()
    {
        var (svc, _, agentRepo, _) = Build();
        agentRepo.GetByIdAsync("ghost", Arg.Any<CancellationToken>())
            .Returns((AgentDefinition?)null);

        Func<Task> act = () => svc.CreateEditDraftAsync("ghost");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateEditDraftAsync_DraftJaExiste_Lanca409()
    {
        var (svc, draftRepo, agentRepo, _) = Build(projectId: "owner");
        agentRepo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(new AgentDefinition
            {
                Id = "agent-x",
                Name = "X",
                Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
                ProjectId = "owner",
            });
        draftRepo.ExistsAsync("agent-x", Arg.Any<CancellationToken>()).Returns(true);

        Func<Task> act = () => svc.CreateEditDraftAsync("agent-x");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Já existe edit-draft*");
    }

    [Fact]
    public async Task CreateEditDraftAsync_CapturaBaseRevisionDoAgent()
    {
        var (svc, draftRepo, agentRepo, versionRepo) = Build(projectId: "owner");
        agentRepo.GetByIdAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(new AgentDefinition
            {
                Id = "agent-x",
                Name = "X",
                Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
                ProjectId = "owner",
            });
        draftRepo.ExistsAsync("agent-x", Arg.Any<CancellationToken>()).Returns(false);
        versionRepo.GetCurrentAsync("agent-x", Arg.Any<CancellationToken>())
            .Returns(BuildAgentVersion("v-3", "agent-x", revision: 3));

        AgentDraft? captured = null;
        draftRepo.UpsertAsync(Arg.Do<AgentDraft>(d => captured = d), null, Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<AgentDraft>());

        await svc.CreateEditDraftAsync("agent-x");

        captured.Should().NotBeNull();
        captured!.BaseAgentId.Should().Be("agent-x");
        captured.BaseRevision.Should().Be(3);
        captured.IsEditDraft.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_DraftDeOutroProjeto_Lanca403()
    {
        var (svc, draftRepo, _, _) = Build(projectId: "p-caller");
        draftRepo.GetByIdAsync("d-1", Arg.Any<CancellationToken>())
            .Returns(new AgentDraft
            {
                Id = "d-1",
                Name = "draft alheio",
                Payload = new AgentDraftPayload(),
                ProjectId = "p-other",
                TenantId = "tenant-a",
            });

        Func<Task> act = () => svc.UpdateAsync("d-1", new AgentDraftPayload(), DateTime.UtcNow);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task SubmitForApprovalAsync_DraftDeOutroProjeto_Lanca403()
    {
        var (svc, draftRepo, _, _) = Build(projectId: "p-caller");
        draftRepo.GetByIdAsync("d-1", Arg.Any<CancellationToken>())
            .Returns(new AgentDraft
            {
                Id = "d-1",
                Name = "draft alheio",
                Payload = new AgentDraftPayload(),
                ProjectId = "p-other",
                TenantId = "tenant-a",
            });

        Func<Task> act = () => svc.SubmitForApprovalAsync("d-1", "actor-x");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task SubmitForApprovalAsync_DraftPropriedade_DelegaProRepo()
    {
        var (svc, draftRepo, _, _) = Build(projectId: "owner");
        draftRepo.GetByIdAsync("d-1", Arg.Any<CancellationToken>())
            .Returns(new AgentDraft
            {
                Id = "d-1",
                Name = "rascunho",
                Payload = new AgentDraftPayload { Name = "X", Model = new AgentModelConfig { DeploymentName = "gpt-4o" } },
                ProjectId = "owner",
                TenantId = "tenant-a",
                Status = AgentDraftStatus.Draft,
            });
        draftRepo.SubmitForApprovalAsync("d-1", "actor-x", Arg.Any<CancellationToken>())
            .Returns(new AgentDraft
            {
                Id = "d-1",
                Name = "rascunho",
                Payload = new AgentDraftPayload(),
                ProjectId = "owner",
                TenantId = "tenant-a",
                Status = AgentDraftStatus.PendingApproval,
                SubmittedAt = DateTime.UtcNow,
            });

        var result = await svc.SubmitForApprovalAsync("d-1", "actor-x");

        result.Draft.Status.Should().Be(AgentDraftStatus.PendingApproval);
        result.Draft.SubmittedAt.Should().NotBeNull();
        await draftRepo.Received(1).SubmitForApprovalAsync("d-1", "actor-x", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NaoExiste_Lanca404()
    {
        var (svc, draftRepo, _, _) = Build();
        draftRepo.GetByIdAsync("ghost", Arg.Any<CancellationToken>())
            .Returns((AgentDraft?)null);

        Func<Task> act = () => svc.DeleteAsync("ghost");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static AgentVersion BuildAgentVersion(string versionId, string agentId, int revision) =>
        new(
            AgentVersionId: versionId,
            AgentDefinitionId: agentId,
            Revision: revision,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: null,
            ChangeReason: null,
            Status: AgentVersionStatus.Published,
            PromptContent: null,
            PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4o", null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, false),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: Array.Empty<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: "hash",
            Tools: Array.Empty<AgentToolSnapshot>());
}
