using EfsAiHub.Host.Api.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EfsAiHub.Infra.Persistence.Postgres;

namespace EfsAiHub.Tests.Integration.Health;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class WorkflowAgentVersionHealthCheckTests(IntegrationWebApplicationFactory factory)
{
    private IAgentVersionRepository VersionRepo => factory.Services.GetRequiredService<IAgentVersionRepository>();
    private IAgentDefinitionRepository AgentRepo => factory.Services.GetRequiredService<IAgentDefinitionRepository>();
    private IAgentService AgentService => factory.Services.GetRequiredService<IAgentService>();
    private IDbContextFactory<AgentFwDbContext> CtxFactory => factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
    private WorkflowAgentVersionHealthCheck Check => new(VersionRepo);

    [Fact]
    public async Task CheckHealth_SemAgents_RetornaHealthy()
    {
        var result = await Check.CheckHealthAsync(new HealthCheckContext());

        // Pode haver orphans de tests anteriores no DB compartilhado da fixture; aceitamos
        // qualquer status que não seja Unhealthy. O caso isolado é coberto abaixo.
        result.Status.Should().NotBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_VersionExisteESemOrphan_NaoListaNaPayload()
    {
        // Cria agent + publica version. Verifica que o version criado não aparece na lista.
        var def = AgentDefinition.Create(
            id: $"agent-healthy-{Guid.NewGuid():N}",
            name: "Agent Healthy",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "x");
        var created = await AgentService.CreateAsync(def);
        var version = await AgentService.PublishVersionAsync(created.Id, breakingChange: false);

        var orphans = await VersionRepo.ListOrphanVersionsAsync(50);
        orphans.Select(o => o.AgentVersionId).Should().NotContain(version.AgentVersionId);
    }

    [Fact]
    public async Task CheckHealth_VersionOrphan_AparesceNaListaERetornaDegraded()
    {
        // Insere version cujo AgentDefinitionId NÃO existe em agent_definitions.
        var orphanAgentId = $"agent-orphan-{Guid.NewGuid():N}";
        var orphanVersionId = Guid.NewGuid().ToString("N");

        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.agent_versions
                (""AgentVersionId"", ""AgentDefinitionId"", ""Revision"", ""CreatedAt"",
                 ""Status"", ""ContentHash"", ""Snapshot"", ""SchemaVersion"")
                VALUES ({0}, {1}, 1, NOW(), 'Published', 'orphan-hash', '{{}}'::jsonb, 1)",
                orphanVersionId, orphanAgentId);
        }

        var orphans = await VersionRepo.ListOrphanVersionsAsync(50);
        orphans.Should().Contain(o => o.AgentVersionId == orphanVersionId);

        var result = await Check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("AgentVersion(s) com agent owner deletado");
        result.Data.Should().ContainKey("agent_version_orphans_count");
        result.Data.Should().ContainKey("sample");
    }

    [Fact]
    public async Task ListOrphanVersions_RespeitaLimit()
    {
        // Insere 3 orphans, limit=2 retorna no máximo 2.
        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            for (int i = 0; i < 3; i++)
            {
                await ctx.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO aihub.agent_versions
                    (""AgentVersionId"", ""AgentDefinitionId"", ""Revision"", ""CreatedAt"",
                     ""Status"", ""ContentHash"", ""Snapshot"", ""SchemaVersion"")
                    VALUES ({0}, {1}, 1, NOW(), 'Published', {2}, '{{}}'::jsonb, 1)",
                    Guid.NewGuid().ToString("N"),
                    $"agent-limit-{Guid.NewGuid():N}",
                    $"hash-{i}");
            }
        }

        var orphans = await VersionRepo.ListOrphanVersionsAsync(limit: 2);
        orphans.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListOrphanVersions_LimitZero_RetornaVazio()
    {
        var orphans = await VersionRepo.ListOrphanVersionsAsync(limit: 0);
        orphans.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckHealth_PayloadIncluiRetiredCount_EmAmbosOsStatus()
    {
        // Healthy path inclui retired_count.
        var result = await Check.CheckHealthAsync(new HealthCheckContext());

        result.Data.Should().ContainKey("agent_version_retired_count");
        // Tipo é int; valor depende do estado do DB compartilhado, mas a chave deve existir.
        result.Data["agent_version_retired_count"].Should().BeOfType<int>();
    }

    [Fact]
    public async Task CountRetiredVersions_RetornaContagemCorreta()
    {
        // Conta retired antes do test pra ter baseline.
        var initialCount = await VersionRepo.CountRetiredVersionsAsync();

        // Insere 2 versions com Status=Retired direto no DB.
        var agentId = $"agent-retired-{Guid.NewGuid():N}";
        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.agent_definitions
                (""Id"", ""Name"", ""Data"", ""ProjectId"", ""Visibility"", ""TenantId"", ""CreatedAt"", ""UpdatedAt"")
                VALUES ({0}, 'Agent Retired', '{{}}'::text, 'default', 'project', 'default', NOW(), NOW())",
                agentId);

            for (int i = 0; i < 2; i++)
            {
                await ctx.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO aihub.agent_versions
                    (""AgentVersionId"", ""AgentDefinitionId"", ""Revision"", ""CreatedAt"",
                     ""Status"", ""ContentHash"", ""Snapshot"", ""SchemaVersion"")
                    VALUES ({0}, {1}, {2}, NOW(), 'Retired', {3}, '{{}}'::jsonb, 1)",
                    Guid.NewGuid().ToString("N"), agentId, i + 1, $"retired-hash-{i}");
            }
        }

        var afterCount = await VersionRepo.CountRetiredVersionsAsync();
        afterCount.Should().Be(initialCount + 2);
    }
}
