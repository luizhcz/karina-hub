using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EfsAiHub.Infra.Persistence.Postgres;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentVersionMigrationTests(IntegrationWebApplicationFactory factory)
{
    private IAgentVersionRepository VersionRepo =>
        factory.Services.GetRequiredService<IAgentVersionRepository>();

    private IDbContextFactory<AgentFwDbContext> CtxFactory =>
        factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();

    [Fact]
    public async Task SchemaVersion_DefaultParaRowsAntigos_FicaEm1()
    {
        // Simula row legacy: insert direto bypassing AppendAsync; sem coluna SchemaVersion explicit.
        var agentId = $"agent-legacy-{Guid.NewGuid():N}";
        var versionId = Guid.NewGuid().ToString("N");

        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.agent_versions
                (""AgentVersionId"", ""AgentDefinitionId"", ""Revision"", ""CreatedAt"",
                 ""Status"", ""ContentHash"", ""Snapshot"")
                VALUES ({0}, {1}, 1, NOW(), 'Published', 'legacy-hash', '{{}}'::jsonb)",
                versionId, agentId);
        }

        var fetched = await VersionRepo.GetByIdAsync(versionId);

        fetched.Should().NotBeNull();
        fetched!.SchemaVersion.Should().Be(1);
        fetched.BreakingChange.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotJsonNull_AcionaFallbackDefensivo()
    {
        // Quando snapshot é JSON null literal, deserializer retorna null e o fallback
        // reconstrói esqueleto a partir das colunas promoted.
        var agentId = $"agent-null-snap-{Guid.NewGuid():N}";
        var versionId = Guid.NewGuid().ToString("N");

        await using (var ctx = await CtxFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO aihub.agent_versions
                (""AgentVersionId"", ""AgentDefinitionId"", ""Revision"", ""CreatedAt"",
                 ""Status"", ""ContentHash"", ""Snapshot"")
                VALUES ({0}, {1}, 7, NOW(), 'Published', 'null-snap-hash', 'null'::jsonb)",
                versionId, agentId);
        }

        var fetched = await VersionRepo.GetByIdAsync(versionId);

        fetched.Should().NotBeNull();
        fetched!.AgentVersionId.Should().Be(versionId);
        fetched.AgentDefinitionId.Should().Be(agentId);
        fetched.Revision.Should().Be(7);
        fetched.ContentHash.Should().Be("null-snap-hash");
        fetched.SchemaVersion.Should().Be(1); // fallback default
    }

    [Fact]
    public async Task NewSnapshot_PersisteSchemaVersion2()
    {
        var agentId = $"agent-v2-{Guid.NewGuid():N}";
        var def = new AgentDefinition
        {
            Id = agentId,
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Description = "Snapshot lossless",
        };
        var version = AgentVersion.FromDefinition(
            def, revision: 1, promptContent: "instr", promptVersionId: null);
        await VersionRepo.AppendAsync(version);

        // Lê coluna promoted direto pra confirmar persistência.
        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT ""SchemaVersion"", ""BreakingChange""
                  FROM aihub.agent_versions
                  WHERE ""AgentDefinitionId"" = @id",
                conn);
            cmd.Parameters.AddWithValue("id", agentId);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();

            reader.GetInt32(0).Should().Be(2);
            reader.IsDBNull(1).Should().BeTrue(); // sem breakingChange explícito → null
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
