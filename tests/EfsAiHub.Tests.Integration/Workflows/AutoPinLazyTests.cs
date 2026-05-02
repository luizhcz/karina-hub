using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Sharing;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Workflows;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AutoPinLazyTests(IntegrationWebApplicationFactory factory)
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

    private async Task<(WorkflowDefinition workflow, AgentDefinition agent)> CreateAgentWithWorkflowAsync()
    {
        var agentId = $"agent-autopin-{Guid.NewGuid():N}";
        var agentDef = AgentDefinition.Create(
            id: agentId,
            name: "Agent Auto-Pin",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "x");
        var agent = await AgentService.CreateAsync(agentDef);

        var workflow = new WorkflowDefinition
        {
            Id = $"wf-autopin-{Guid.NewGuid():N}",
            Name = "Workflow Auto-Pin",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents =
            [
                new WorkflowAgentReference { AgentId = agentId },
            ],
        };
        var saved = await WorkflowService.CreateAsync(workflow);
        return (saved, agent);
    }

    [Fact]
    public async Task AutoPin_RefSemPin_PreencheCurrentVersionId()
    {
        var (workflow, agent) = await CreateAgentWithWorkflowAsync();
        var current = await VersionRepo.GetCurrentAsync(agent.Id);
        current.Should().NotBeNull();

        // Agent ref começa sem pin.
        workflow.Agents[0].AgentVersionId.Should().BeNullOrEmpty();

        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        // In-memory ref agora tem pin.
        workflow.Agents[0].AgentVersionId.Should().Be(current!.AgentVersionId);

        // Persistido no DB.
        var fresh = await WorkflowRepo.GetByIdAsync(workflow.Id);
        fresh.Should().NotBeNull();
        fresh!.Agents[0].AgentVersionId.Should().Be(current.AgentVersionId);
    }

    [Fact]
    public async Task AutoPin_RefJaPinada_NoOpSemAuditDuplicado()
    {
        var (workflow, agent) = await CreateAgentWithWorkflowAsync();
        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        var auditAfterFirst = await CountAuditRowsAsync(
            AdminAuditActions.WorkflowAgentVersionAutoPinned, workflow.Id);
        auditAfterFirst.Should().BeGreaterThanOrEqualTo(1);

        // Segunda execução: ref já tem pin → no-op.
        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        var auditAfterSecond = await CountAuditRowsAsync(
            AdminAuditActions.WorkflowAgentVersionAutoPinned, workflow.Id);
        auditAfterSecond.Should().Be(auditAfterFirst);
    }

    [Fact]
    public async Task AutoPin_AgentSemVersionPublished_PulaSemErro()
    {
        // Insere agent direto no DB sem nenhuma version.
        var orphanAgentId = $"agent-no-version-{Guid.NewGuid():N}";
        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.agent_definitions
                (""Id"", ""Name"", ""Data"", ""ProjectId"", ""Visibility"", ""TenantId"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ({0}, 'Orphan', '{{}}'::text, 'default', 'project', 'default', NOW(), NOW())",
                orphanAgentId);
        }

        var workflow = new WorkflowDefinition
        {
            Id = $"wf-orphan-{Guid.NewGuid():N}",
            Name = "Workflow Orphan",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents = [new WorkflowAgentReference { AgentId = orphanAgentId }],
        };

        // Insert workflow direto no DB pra evitar validação.
        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            var data = System.Text.Json.JsonSerializer.Serialize(workflow,
                EfsAiHub.Core.Abstractions.Persistence.JsonDefaults.Domain);
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.workflow_definitions
                (""Id"", ""Name"", ""Data"", ""ProjectId"", ""Visibility"", ""TenantId"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ({0}, 'WF Orphan', {1}::text, 'default', 'project', 'default', NOW(), NOW())",
                workflow.Id, data);
        }

        // Auto-pin não deve lançar; ref fica sem pin (logged warning).
        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        workflow.Agents[0].AgentVersionId.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AutoPin_WorkflowInexistente_LogaWarningSemLancar()
    {
        var phantom = new WorkflowDefinition
        {
            Id = $"wf-phantom-{Guid.NewGuid():N}",
            Name = "Phantom",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = "default",
            Agents = [new WorkflowAgentReference { AgentId = "agent-x" }],
        };

        var act = async () => await AutoPinService.AutoPinLegacyReferencesAsync(phantom);
        await act.Should().NotThrowAsync();

        // Workflow não foi criado no DB; ref continua sem pin.
        phantom.Agents[0].AgentVersionId.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AutoPin_RefSemPinComVersionRecemPublicada_PinaCorreto()
    {
        var (workflow, agent) = await CreateAgentWithWorkflowAsync();

        // Publica nova version do agent (será o "current" após este publish).
        var v2 = await AgentService.PublishVersionAsync(agent.Id, breakingChange: false);
        var expectedPinId = v2.AgentVersionId;

        await AutoPinService.AutoPinLegacyReferencesAsync(workflow);

        workflow.Agents[0].AgentVersionId.Should().Be(expectedPinId);
    }

    [Fact]
    public async Task AutoPin_DuasInstanciasParalelas_ResultaIdempotenteSemPinDivergente()
    {
        var (workflow, agent) = await CreateAgentWithWorkflowAsync();

        // Duas instâncias do service rodando em paralelo (simula 2 pods).
        var workflowCopyA = await WorkflowRepo.GetByIdAsync(workflow.Id);
        var workflowCopyB = await WorkflowRepo.GetByIdAsync(workflow.Id);

        await Task.WhenAll(
            AutoPinService.AutoPinLegacyReferencesAsync(workflowCopyA!),
            AutoPinService.AutoPinLegacyReferencesAsync(workflowCopyB!));

        var fresh = await WorkflowRepo.GetByIdAsync(workflow.Id);
        var current = await VersionRepo.GetCurrentAsync(agent.Id);
        fresh!.Agents[0].AgentVersionId.Should().Be(current!.AgentVersionId);
    }
}
