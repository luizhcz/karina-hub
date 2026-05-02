using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Agents;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentPublishVersionTests(IntegrationWebApplicationFactory factory)
{
    private IAgentService AgentService => factory.Services.GetRequiredService<IAgentService>();
    private IAgentDefinitionRepository AgentRepo => factory.Services.GetRequiredService<IAgentDefinitionRepository>();
    private IAgentVersionRepository VersionRepo => factory.Services.GetRequiredService<IAgentVersionRepository>();
    private IProjectContextAccessor ProjectAccessor => factory.Services.GetRequiredService<IProjectContextAccessor>();
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

    private async Task<AgentDefinition> CreateAgentAsync(string instructions = "instr")
    {
        var def = AgentDefinition.Create(
            id: $"agent-publish-{Guid.NewGuid():N}",
            name: "Agent Publish Test",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: instructions);
        // CreateAsync via service preenche ProjectId pelo accessor.
        return await AgentService.CreateAsync(def);
    }

    [Fact]
    public async Task Publish_BreakingTrueComChangeReason_CriaVersionMarcadaBreaking()
    {
        var def = await CreateAgentAsync("v1");
        var version = await AgentService.PublishVersionAsync(
            def.Id,
            breakingChange: true,
            changeReason: "schema do output mudou",
            createdBy: "user-1");

        version.BreakingChange.Should().BeTrue();
        version.ChangeReason.Should().Be("schema do output mudou");
        version.CreatedBy.Should().Be("user-1");
        version.SchemaVersion.Should().Be(2);
    }

    [Fact]
    public async Task Publish_BreakingTrueSemChangeReason_LancaDomainException()
    {
        var def = await CreateAgentAsync();

        var act = async () => await AgentService.PublishVersionAsync(
            def.Id, breakingChange: true, changeReason: null);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*BreakingChange=true exige ChangeReason*");
    }

    [Fact]
    public async Task Publish_BreakingFalseSemChangeReason_Aceita()
    {
        var def = await CreateAgentAsync("v1");

        var version = await AgentService.PublishVersionAsync(
            def.Id, breakingChange: false, changeReason: null);

        version.BreakingChange.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_AgenteInexistente_LancaKeyNotFound()
    {
        var act = async () => await AgentService.PublishVersionAsync(
            "agent-fantasma", breakingChange: false);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Publish_Sucesso_EmiteAuditAgentVersionPublished()
    {
        var def = await CreateAgentAsync("audit test");
        var version = await AgentService.PublishVersionAsync(
            def.Id, breakingChange: true, changeReason: "schema mudou", createdBy: "user-audit");

        var count = await CountAuditRowsAsync(AdminAuditActions.AgentVersionPublished, def.Id);
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Publish_NoOpPorIdempotencia_NaoEmiteAuditDuplicado()
    {
        var def = await CreateAgentAsync("idempotent audit");

        await AgentService.PublishVersionAsync(def.Id, breakingChange: false);
        var afterFirst = await CountAuditRowsAsync(AdminAuditActions.AgentVersionPublished, def.Id);

        // Re-publish sem mudança no conteúdo: idempotente, audit count deve ficar igual.
        await AgentService.PublishVersionAsync(def.Id, breakingChange: false);
        var afterSecond = await CountAuditRowsAsync(AdminAuditActions.AgentVersionPublished, def.Id);

        afterSecond.Should().Be(afterFirst);
    }

    [Fact]
    public async Task Publish_IdempotenciaPorContentHash_RetornaExistingSemNovaRevision()
    {
        // CreateAsync gera auto-snapshot via UpsertAsync ANTES do SeedInitialPromptAsync —
        // então o snapshot inicial tem promptContent=null. Primeira PublishVersionAsync
        // captura prompt já seedado (ContentHash difere → nova version). Segunda
        // PublishVersionAsync é idempotente (ContentHash bate com a anterior).
        var def = await CreateAgentAsync("idempotent test");

        var firstPublish = await AgentService.PublishVersionAsync(def.Id, breakingChange: false);
        var afterFirst = await VersionRepo.ListByDefinitionAsync(def.Id);
        var countAfterFirst = afterFirst.Count;

        var secondPublish = await AgentService.PublishVersionAsync(def.Id, breakingChange: false);
        var afterSecond = await VersionRepo.ListByDefinitionAsync(def.Id);

        afterSecond.Count.Should().Be(countAfterFirst);
        secondPublish.AgentVersionId.Should().Be(firstPublish.AgentVersionId);
        secondPublish.ContentHash.Should().Be(firstPublish.ContentHash);
    }
}
