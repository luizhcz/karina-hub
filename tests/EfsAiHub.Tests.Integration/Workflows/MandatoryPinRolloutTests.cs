using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Workflows;

/// <summary>
/// Cobre cenários ponta-a-ponta de pin obrigatório: auto-pin lazy converte
/// workflow legacy; PublishVersion declara breaking/patch e o snapshot persiste
/// o flag corretamente. Enforcement do <c>Sharing:MandatoryPin=true</c> via
/// validator é coberto em <c>WorkflowValidatorMandatoryPinTests</c> (unit).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class MandatoryPinRolloutTests(IntegrationWebApplicationFactory factory)
{
    private IAgentService AgentService => factory.Services.GetRequiredService<IAgentService>();
    private IWorkflowService WorkflowService => factory.Services.GetRequiredService<IWorkflowService>();
    private IWorkflowAutoPinService AutoPinService => factory.Services.GetRequiredService<IWorkflowAutoPinService>();
    private IWorkflowDefinitionRepository WorkflowRepo => factory.Services.GetRequiredService<IWorkflowDefinitionRepository>();
    private IAgentVersionRepository VersionRepo => factory.Services.GetRequiredService<IAgentVersionRepository>();
    private IDbContextFactory<AgentFwDbContext> CtxFactory => factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();

    private async Task<int> CountAuditRowsAsync(string action, string resourceId)
    {
        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT COUNT(*) FROM aihub.admin_audit_log
                  WHERE ""Action"" = @action AND ""ResourceId"" = @resource",
                conn);
            cmd.Parameters.AddWithValue("action", action);
            cmd.Parameters.AddWithValue("resource", resourceId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }
        finally { await conn.CloseAsync(); }
    }

    [Fact]
    public async Task Rollout_WorkflowLegacy_AutoPinLazyConvertePrimeiroExecute()
    {
        // 1. Cria agent + workflow LEGACY (sem pin).
        var agentId = $"agent-rollout-{Guid.NewGuid():N}";
        var agent = await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Rollout",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "x"));

        var workflow = await WorkflowService.CreateAsync(new WorkflowDefinition
        {
            Id = $"wf-rollout-{Guid.NewGuid():N}",
            Name = "Workflow Rollout",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents = [new WorkflowAgentReference { AgentId = agentId }],
        });
        workflow.Agents[0].AgentVersionId.Should().BeNullOrEmpty();

        // 2. Auto-pin lazy converte (simula first AgentFactory call após MandatoryPin=true).
        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        // 3. Verifica pin populado, audit emitido, métrica disponível.
        var current = await VersionRepo.GetCurrentAsync(agentId);
        workflow.Agents[0].AgentVersionId.Should().Be(current!.AgentVersionId);

        var fresh = await WorkflowRepo.GetByIdAsync(workflow.Id);
        fresh!.Agents[0].AgentVersionId.Should().Be(current.AgentVersionId);

        var auditCount = await CountAuditRowsAsync(
            AdminAuditActions.WorkflowAgentVersionAutoPinned, workflow.Id);
        auditCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Rollout_WorkflowJaPinado_ResaveSemMudancaPreservaPin()
    {
        var agentId = $"agent-preserve-{Guid.NewGuid():N}";
        var agent = await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Preserve",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "x"));
        var current = await VersionRepo.GetCurrentAsync(agentId);

        var workflow = await WorkflowService.CreateAsync(new WorkflowDefinition
        {
            Id = $"wf-preserve-{Guid.NewGuid():N}",
            Name = "Workflow Preserve",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents =
            [
                new WorkflowAgentReference { AgentId = agentId, AgentVersionId = current!.AgentVersionId },
            ],
        });

        // Auto-pin no-op (já pinado).
        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        var fresh = await WorkflowRepo.GetByIdAsync(workflow.Id);
        fresh!.Agents[0].AgentVersionId.Should().Be(current.AgentVersionId);
    }

    [Fact]
    public async Task Rollout_PublishVersionDeclaraBreakingChange_PersisteFlagNaRow()
    {
        // E2E: caller usa AgentService.UpdateAsync com breakingChange=true.
        // Snapshot resultante deve ter BreakingChange=true persistido na promoted column.
        var agentId = $"agent-declared-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Declared",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v1"));

        var v1 = await VersionRepo.GetCurrentAsync(agentId);

        // PUT com novo conteúdo + breakingChange explícito.
        var def = AgentDefinition.Create(
            id: agentId,
            name: "Agent Declared",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v2 — new schema");
        await AgentService.UpdateAsync(def,
            breakingChange: true,
            changeReason: "schema mudou",
            createdBy: "user-test");

        var v2 = await VersionRepo.GetCurrentAsync(agentId);
        v2.Should().NotBeNull();
        v2!.AgentVersionId.Should().NotBe(v1!.AgentVersionId);
        v2.BreakingChange.Should().BeTrue();
        v2.ChangeReason.Should().Be("schema mudou");
        v2.CreatedBy.Should().Be("user-test");
    }

    [Fact]
    public async Task Rollout_PublishPatch_PersisteFlagFalse()
    {
        var agentId = $"agent-patch-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Patch",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v1"));

        var def = AgentDefinition.Create(
            id: agentId,
            name: "Agent Patch",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v2 — typo fix");
        await AgentService.UpdateAsync(def,
            breakingChange: false,
            changeReason: "fix typo");

        var current = await VersionRepo.GetCurrentAsync(agentId);
        current!.BreakingChange.Should().BeFalse();
    }

    [Fact]
    public async Task Rollout_PublishLegacySemIntent_PersisteBreakingChangeNull()
    {
        // Caller que NÃO declara intent (request DTO sem BreakingChange) → snapshot null.
        // Auto-snapshot via UpsertAsync sem breakingChange explícito mantém o flag null.
        var agentId = $"agent-legacy-publish-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Legacy Publish",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v1"));

        var current = await VersionRepo.GetCurrentAsync(agentId);
        current!.BreakingChange.Should().BeNull();
    }
}
