using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Agents;

/// <summary>
/// Cobre a regra de produto "Router não passa por aprovação":
///   - submit de draft Router → AutoApproved=true, Tier=TypeBypass.
///   - history registra Action=AutoApproved, Tier=TypeBypass, ChangeReason=user.
///   - create de Router força Visibility=global (invariante de produto).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentRouterDraftBypassTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly IntegrationWebApplicationFactory _factory = factory;

    private IDbContextFactory<AgentFwDbContext> CtxFactory =>
        _factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
    private IAgentService AgentService =>
        _factory.Services.GetRequiredService<IAgentService>();

    private static object BuildRouterDraftPayload(string id) => new
    {
        id,
        payload = new
        {
            name = $"Router Bypass Test {id}",
            type = "Router",
            model = new { deploymentName = "gpt-4o" },
            instructions = "Roteie a mensagem entre as intents disponíveis.",
            // Router exige output estruturado com intent — sem isso publish quebra
            // antes de chegarmos no auto-approve.
            structuredOutput = new
            {
                mode = "json_schema",
                schema = "{\"type\":\"object\",\"properties\":{\"intent\":{\"type\":\"string\"},\"confidence\":{\"type\":\"number\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"intent\"]}",
            },
        },
    };

    [Fact]
    public async Task SubmitDraft_RouterType_ReturnsAutoApprovedWithTypeBypassTier()
    {
        var id = $"draft-router-{Guid.NewGuid():N}";
        var create = await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var submit = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Adicionei intent de cancelamento de boleta no router." });

        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("autoApproved").GetBoolean().Should().BeTrue(
            "Router bypassa aprovação por regra de produto");
        body.GetProperty("tier").GetString().Should().Be("TypeBypass");
    }

    [Fact]
    public async Task SubmitDraft_RouterType_PersistsAgentWithGlobalVisibility()
    {
        // Cobre a invariante: a publicação de um Router (mesmo via draft) força
        // Visibility=global no agent_definitions. O draft é new (sem BaseAgentId),
        // então o publish dispara AgentService.CreateAsync que aplica a regra.
        var id = $"draft-router-vis-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        var submit = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Publicação inicial do router de boleta." });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT ""Visibility"" FROM aihub.agent_definitions WHERE ""Id"" = @id",
                conn);
            cmd.Parameters.AddWithValue("id", id);
            var result = await cmd.ExecuteScalarAsync();
            result?.ToString().Should().Be("global",
                "Router deve persistir como global (invariante AgentService.CreateAsync)");
        }
        finally { await conn.CloseAsync(); }
    }

    [Fact]
    public async Task SubmitDraft_RouterType_RecordsAutoApprovedActionInHistory()
    {
        var id = $"draft-router-hist-{Guid.NewGuid():N}";
        const string reason = "Reorganizei intents pra cobrir o fluxo do assessor.";
        await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = reason });

        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT ""Action"", ""Tier"", ""Feedback"" FROM aihub.agent_approval_history
                  WHERE ""DraftId"" = @draftId ORDER BY ""OccurredAt"" DESC LIMIT 1",
                conn);
            cmd.Parameters.AddWithValue("draftId", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue("history deve ter ao menos uma entry pro draft");
            reader.GetString(0).Should().Be("AutoApproved");
            reader.GetString(1).Should().Be("TypeBypass");
            // ChangeReason persiste como Feedback no schema atual de history.
            reader.IsDBNull(2).Should().BeFalse();
            reader.GetString(2).Should().Contain(reason);
        }
        finally { await conn.CloseAsync(); }
    }

}
