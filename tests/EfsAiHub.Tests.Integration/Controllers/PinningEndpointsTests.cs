using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class PinningEndpointsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private IAgentService AgentService => factory.Services.GetRequiredService<IAgentService>();
    private IWorkflowService WorkflowService => factory.Services.GetRequiredService<IWorkflowService>();
    private IAgentVersionRepository VersionRepo => factory.Services.GetRequiredService<IAgentVersionRepository>();
    private IDbContextFactory<AgentFwDbContext> CtxFactory => factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();

    private async Task<(string agentId, string workflowId)> SetupAgentAndWorkflowAsync()
    {
        var agentId = $"agent-pinning-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Pinning",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "x"));

        var workflow = await WorkflowService.CreateAsync(new WorkflowDefinition
        {
            Id = $"wf-pinning-{Guid.NewGuid():N}",
            Name = "Workflow Pinning",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents = [new WorkflowAgentReference { AgentId = agentId }],
        });

        return (agentId, workflow.Id);
    }

    // ── POST /api/agents/{id}/versions (HTTP) ───────────────────────────────
    // AgentsController DI chain não toca AWS Secrets Manager — endpoints HTTP testáveis.

    [Fact]
    public async Task PublishVersion_BreakingTrueComChangeReason_Retorna201()
    {
        var (agentId, _) = await SetupAgentAndWorkflowAsync();

        var resp = await _client.PostAsJsonAsync(
            $"/api/agents/{agentId}/versions",
            new { breakingChange = true, changeReason = "schema mudou" });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("breakingChange").GetBoolean().Should().BeTrue();
        body.GetProperty("changeReason").GetString().Should().Be("schema mudou");
    }

    [Fact]
    public async Task PublishVersion_BreakingTrueSemChangeReason_Retorna400()
    {
        var (agentId, _) = await SetupAgentAndWorkflowAsync();

        var resp = await _client.PostAsJsonAsync(
            $"/api/agents/{agentId}/versions",
            new { breakingChange = true });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PublishVersion_AgentInexistente_Retorna404()
    {
        var resp = await _client.PostAsJsonAsync(
            $"/api/agents/agent-fantasma-{Guid.NewGuid():N}/versions",
            new { breakingChange = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── IWorkflowAgentVersionStatusService (service-level) ──────────────────
    // Testado via service direto (bypass WorkflowsController) — DI chain do
    // controller dispara AWS Secrets Manager init (issue pre-existing da fixture).
    // A lógica de patch propagation + breaking detection é o que importa pra UI.

    [Fact]
    public async Task GetStatus_RefSemPin_HasUpdateFalse()
    {
        var (agentId, workflowId) = await SetupAgentAndWorkflowAsync();
        var statusService = factory.Services.GetRequiredService<IWorkflowAgentVersionStatusService>();

        var status = await statusService.GetStatusAsync(workflowId);

        status.Should().HaveCount(1);
        var entry = status[0];
        entry.AgentId.Should().Be(agentId);
        entry.PinnedVersionId.Should().BeNull();
        entry.HasUpdate.Should().BeFalse();
        entry.IsPinnedBlockedByBreaking.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_PinAtual_HasUpdateFalseEChangesVazio()
    {
        var (agentId, workflowId) = await SetupAgentAndWorkflowAsync();
        var current = await VersionRepo.GetCurrentAsync(agentId);

        // Pin via repo direto (controller endpoint tem AWS DI issue).
        var workflowRepo = factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
        var workflow = await workflowRepo.GetByIdAsync(workflowId);
        workflow!.Agents[0].AgentVersionId = current!.AgentVersionId;
        await workflowRepo.UpsertAsync(workflow);

        var statusService = factory.Services.GetRequiredService<IWorkflowAgentVersionStatusService>();
        var status = await statusService.GetStatusAsync(workflowId);
        var entry = status[0];

        entry.PinnedVersionId.Should().Be(current.AgentVersionId);
        entry.HasUpdate.Should().BeFalse();
        entry.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatus_PinAncestorSemBreaking_BlockedFalseEHasUpdateTrue()
    {
        var (agentId, workflowId) = await SetupAgentAndWorkflowAsync();
        var v1 = await VersionRepo.GetCurrentAsync(agentId);

        // Pin v1.
        var workflowRepo = factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
        var workflow = await workflowRepo.GetByIdAsync(workflowId);
        workflow!.Agents[0].AgentVersionId = v1!.AgentVersionId;
        await workflowRepo.UpsertAsync(workflow);

        // Publica 2 patches (sem breaking) — workflow pode propagar livre.
        var defV2 = AgentDefinition.Create(
            id: agentId, name: "Agent Pinning",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v2 patch");
        await AgentService.UpdateAsync(defV2, breakingChange: false, changeReason: "v2 patch");

        var defV3 = AgentDefinition.Create(
            id: agentId, name: "Agent Pinning",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v3 patch");
        await AgentService.UpdateAsync(defV3, breakingChange: false, changeReason: "v3 patch");

        var statusService = factory.Services.GetRequiredService<IWorkflowAgentVersionStatusService>();
        var status = await statusService.GetStatusAsync(workflowId);
        var entry = status[0];

        entry.HasUpdate.Should().BeTrue();
        entry.IsPinnedBlockedByBreaking.Should().BeFalse();
        entry.Changes.Should().NotBeEmpty();
        entry.Changes.Should().OnlyContain(c => c.BreakingChange != true);
    }

    [Fact]
    public async Task GetStatus_PinAncestorComBreaking_BlockedTrueEChangesPopulado()
    {
        var (agentId, workflowId) = await SetupAgentAndWorkflowAsync();
        var v1 = await VersionRepo.GetCurrentAsync(agentId);

        // Pin v1.
        var workflowRepo = factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
        var workflow = await workflowRepo.GetByIdAsync(workflowId);
        workflow!.Agents[0].AgentVersionId = v1!.AgentVersionId;
        await workflowRepo.UpsertAsync(workflow);

        // Publica v2 breaking + v3 patch (via service direto).
        await AgentService.PublishVersionAsync(agentId, breakingChange: true, changeReason: "v2 breaking");
        var defV3 = AgentDefinition.Create(
            id: agentId, name: "Agent Pinning",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v3 prompt distinct");
        await AgentService.UpdateAsync(defV3, breakingChange: false, changeReason: "v3 patch");

        var statusService = factory.Services.GetRequiredService<IWorkflowAgentVersionStatusService>();
        var status = await statusService.GetStatusAsync(workflowId);
        var entry = status[0];

        entry.HasUpdate.Should().BeTrue();
        entry.IsPinnedBlockedByBreaking.Should().BeTrue();
        entry.Changes.Should().NotBeEmpty();
        entry.Changes.Should().Contain(c => c.BreakingChange == true);
    }

    [Fact]
    public async Task GetStatus_WorkflowInexistente_LancaKeyNotFound()
    {
        var statusService = factory.Services.GetRequiredService<IWorkflowAgentVersionStatusService>();

        var act = async () => await statusService.GetStatusAsync($"wf-fantasma-{Guid.NewGuid():N}");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAgentPin_ViaRepo_PersisteEAuditPodeSerInjetadoSeparadamente()
    {
        // Lógica essencial do PATCH endpoint: workflow lookup + ref existe + version válida
        // + persiste. Audit é orquestrado pelo controller separadamente (cobertura via
        // PublishVersion_* tests do AgentsController, mesmo padrão).
        var (agentId, workflowId) = await SetupAgentAndWorkflowAsync();
        var current = await VersionRepo.GetCurrentAsync(agentId);

        var workflowRepo = factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
        var workflow = await workflowRepo.GetByIdAsync(workflowId);
        workflow.Should().NotBeNull();
        workflow!.Agents[0].AgentVersionId.Should().BeNullOrEmpty();

        workflow.Agents[0].AgentVersionId = current!.AgentVersionId;
        await workflowRepo.UpsertAsync(workflow);

        var fresh = await workflowRepo.GetByIdAsync(workflowId);
        fresh!.Agents[0].AgentVersionId.Should().Be(current.AgentVersionId);
    }
}
