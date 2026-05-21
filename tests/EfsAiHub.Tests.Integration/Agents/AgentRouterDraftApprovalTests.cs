using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Agents;

/// <summary>
/// Cobre o gate de approval do Router (paritário com os demais tipos):
///   - submit de draft Router → autoApproved=false, draft fica PendingApproval.
///   - approve via /agent-approvals → publica em agent_definitions com
///     Visibility=global (invariante do AgentService).
///   - history registra Submitted e Approved em ordem cronológica.
///   - reject → draft volta a Rejected com RejectionFeedback.
///   - edit-draft cosmético (só Description) auto-aprova com Tier=Cosmetic
///     (regra ortogonal ao tipo do agente).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentRouterDraftApprovalTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly IntegrationWebApplicationFactory _factory = factory;

    private IDbContextFactory<AgentFwDbContext> CtxFactory =>
        _factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();
    private IAgentService AgentService =>
        _factory.Services.GetRequiredService<IAgentService>();

    private static object BuildRouterDraftPayload(string id, string? description = null) => new
    {
        id,
        payload = new
        {
            name = $"Router Approval Test {id}",
            description,
            type = "Router",
            model = new { deploymentName = "gpt-4o" },
            instructions = "Roteie a mensagem entre as intents disponíveis.",
            // Router exige output estruturado com intent — sem isso o publish quebra
            // antes da fila de aprovação completar.
            structuredOutput = new
            {
                mode = "json_schema",
                schema = "{\"type\":\"object\",\"properties\":{\"intent\":{\"type\":\"string\"},\"confidence\":{\"type\":\"number\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"intent\"]}",
            },
        },
    };

    [Fact]
    public async Task SubmitDraft_RouterType_ReturnsPendingApprovalWithBehavioralTier()
    {
        var id = $"draft-router-{Guid.NewGuid():N}";
        var create = await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var submit = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Adicionei intent de cancelamento de boleta no router." });

        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("autoApproved").GetBoolean().Should().BeFalse(
            "Router segue o mesmo gate de revisão humana que os demais tipos");
        body.GetProperty("tier").GetString().Should().Be("Behavioral");
    }

    [Fact]
    public async Task ApproveRouterDraft_PersistsAgentWithGlobalVisibility()
    {
        // Cobre o gate completo: submit não publica, só approve publica. A
        // invariante Visibility=global do AgentService permanece — agnóstica do
        // approval flow.
        var id = $"draft-router-vis-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Publicação inicial do router de boleta." });

        (await AgentExistsAsync(id)).Should().BeFalse(
            "submit do Router não deve publicar — agent só existe após approve");

        var approve = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{id}/approve",
            new { changeReason = "Revisado pelo time de governança." });
        approve.StatusCode.Should().Be(HttpStatusCode.Created);

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
    public async Task ApproveRouterDraft_RecordsSubmittedAndApprovedInHistory()
    {
        var id = $"draft-router-hist-{Guid.NewGuid():N}";
        const string submitReason = "Reorganizei intents pra cobrir o fluxo do assessor.";
        const string approveReason = "Revisão concluída pelo arquiteto.";
        await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = submitReason });
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{id}/approve",
            new { changeReason = approveReason });

        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT ""Action"", ""Tier"", ""Feedback"" FROM aihub.agent_approval_history
                  WHERE ""DraftId"" = @draftId ORDER BY ""OccurredAt"" ASC",
                conn);
            cmd.Parameters.AddWithValue("draftId", id);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue("history deve ter entry de Submitted");
            reader.GetString(0).Should().Be("Submitted");
            reader.IsDBNull(1).Should().BeTrue("Submitted não tem Tier");

            (await reader.ReadAsync()).Should().BeTrue("history deve ter entry de Approved");
            reader.GetString(0).Should().Be("Approved");
            reader.IsDBNull(2).Should().BeFalse();
            reader.GetString(2).Should().Contain(approveReason);
        }
        finally { await conn.CloseAsync(); }
    }

    [Fact]
    public async Task RejectRouterDraft_RevertsToDraftWithFeedback()
    {
        var id = $"draft-router-reject-{Guid.NewGuid():N}";
        const string feedback = "Falta o intent de cancelamento; refaça antes de reenviar.";
        await _client.PostAsJsonAsync("/api/aihub/agent-drafts", BuildRouterDraftPayload(id));
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Primeira tentativa de publicação." });

        var reject = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{id}/reject",
            new { feedback });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejectBody = await reject.Content.ReadFromJsonAsync<JsonElement>();
        rejectBody.GetProperty("status").GetString().Should().Be("Rejected");
        rejectBody.GetProperty("rejectionFeedback").GetString().Should().Be(feedback);

        (await AgentExistsAsync(id)).Should().BeFalse(
            "draft rejeitado não deve aparecer em agent_definitions");
    }

    [Fact]
    public async Task SubmitEditDraft_RouterCosmeticChange_AutoApprovesAsCosmetic()
    {
        // Paridade da regra Cosmetic vs Behavioral é por mudança, não por tipo.
        // Edit-draft de Router que só altera Description segue auto-aprovando.
        var id = $"draft-router-cosmetic-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync(
            "/api/aihub/agent-drafts",
            BuildRouterDraftPayload(id, description: "Versão inicial."));
        await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Publicação inicial." });
        var approveInitial = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{id}/approve",
            new { changeReason = "Aprovado pra teste de edição cosmética." });
        approveInitial.StatusCode.Should().Be(HttpStatusCode.Created);

        // Fork pra edit-draft e altera somente Description.
        var fork = await _client.PostAsync($"/api/aihub/agents/{id}/edit-draft", content: null);
        fork.StatusCode.Should().Be(HttpStatusCode.Created);
        var draftAfterFork = await fork.Content.ReadFromJsonAsync<JsonElement>();
        var updatedAt = draftAfterFork.GetProperty("updatedAt").GetString();

        var update = await _client.PutAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}",
            new
            {
                expectedUpdatedAt = updatedAt,
                payload = MergeDescription(draftAfterFork.GetProperty("payload"), "Descrição atualizada — só cosmético."),
            });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var submit = await _client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{id}/submit",
            new { changeReason = "Atualização de descrição." });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("autoApproved").GetBoolean().Should().BeTrue(
            "edit-draft cosmético segue auto-aprovando independentemente do tipo");
        body.GetProperty("tier").GetString().Should().Be("Cosmetic");
    }

    private async Task<bool> AgentExistsAsync(string id)
    {
        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT 1 FROM aihub.agent_definitions WHERE ""Id"" = @id",
                conn);
            cmd.Parameters.AddWithValue("id", id);
            var result = await cmd.ExecuteScalarAsync();
            return result is not null;
        }
        finally { await conn.CloseAsync(); }
    }

    private static object MergeDescription(JsonElement payload, string newDescription)
    {
        // Reconstrói o payload preservando todos os campos do fork e sobrescrevendo
        // só Description — garante diff classificado como Cosmetic.
        using var doc = JsonDocument.Parse(payload.GetRawText());
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
        }
        dict["description"] = newDescription;
        return dict;
    }
}
