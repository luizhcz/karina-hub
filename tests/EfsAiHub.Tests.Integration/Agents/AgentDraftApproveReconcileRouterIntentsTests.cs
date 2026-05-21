using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Agents;

/// <summary>
/// Garante que o approve de draft Router reconcilia a junction
/// <c>aihub.agent_router_intents</c> com o set declarado em
/// <c>AgentDraftPayload.RouterIntentIds</c>. Sem essa reconciliação, o agent
/// publicado fica sem nenhuma intent — apesar do draft tê-las.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentDraftApproveReconcileRouterIntentsTests(IntegrationWebApplicationFactory factory)
{
    private readonly IntegrationWebApplicationFactory _factory = factory;

    private IDbContextFactory<AgentFwDbContext> CtxFactory =>
        _factory.Services.GetRequiredService<IDbContextFactory<AgentFwDbContext>>();

    private static object BuildRouterDraftPayload(string id, IReadOnlyList<string> intentIds) => new
    {
        id,
        payload = new
        {
            name = $"Router approve reconcile {id}",
            type = "Router",
            instructions = "Roteie a mensagem entre as intents disponíveis.",
            model = new { deploymentName = "gpt-4o-mini" },
            structuredOutput = new
            {
                mode = "json_schema",
                schema = "{\"type\":\"object\",\"properties\":{\"intent\":{\"type\":\"string\"}},\"required\":[\"intent\"]}",
            },
            routerIntentIds = intentIds,
        },
    };

    private async Task<HashSet<string>> ReadJunctionAsync(string agentId)
    {
        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(
                @"SELECT ""IntentId"" FROM aihub.agent_router_intents WHERE ""AgentId"" = @id",
                conn);
            cmd.Parameters.AddWithValue("id", agentId);
            var result = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(reader.GetString(0));
            return result;
        }
        finally { await conn.CloseAsync(); }
    }

    private async Task SeedProjectAndIntentsAsync(string projectId, IReadOnlyDictionary<string, string> intents)
    {
        // O schema do test container não traz os seeds — semeia o projeto da
        // FK e as intents direto via SQL pra evitar dependência do endpoint
        // POST /router-intents (cobre só o caminho de approve aqui).
        await using var ctx = await CtxFactory.CreateDbContextAsync();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using (var projCmd = new NpgsqlCommand(
                @"INSERT INTO aihub.projects (id, name, tenant_id, settings, created_at, updated_at)
                  VALUES (@id, @name, 'default', '{}'::jsonb, NOW(), NOW())
                  ON CONFLICT (id) DO NOTHING",
                conn))
            {
                projCmd.Parameters.AddWithValue("id", projectId);
                projCmd.Parameters.AddWithValue("name", $"Project {projectId}");
                await projCmd.ExecuteNonQueryAsync();
            }

            foreach (var (id, name) in intents)
            {
                await using var cmd = new NpgsqlCommand(
                    @"INSERT INTO aihub.router_intents
                        (""Id"", ""TenantId"", ""ProjectId"", ""Name"", ""DisplayName"", ""Description"", ""Examples"", ""IsSystem"", ""CreatedAt"", ""UpdatedAt"")
                      VALUES
                        (@id, 'default', @projectId, @name, @display, @desc, '[]'::jsonb, FALSE, NOW(), NOW())
                      ON CONFLICT (""Id"") DO NOTHING",
                    conn);
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("projectId", projectId);
                cmd.Parameters.AddWithValue("name", name);
                cmd.Parameters.AddWithValue("display", $"Intent {name}");
                cmd.Parameters.AddWithValue("desc", $"Intent {name} para teste de reconciliação no approve.");
                await cmd.ExecuteNonQueryAsync();
            }
        }
        finally { await conn.CloseAsync(); }
    }

    private HttpClient ClientForProject(string projectId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Remove("x-project-id");
        client.DefaultRequestHeaders.Add("x-project-id", projectId);
        return client;
    }

    [Fact]
    public async Task ApproveDraft_RouterWithIntentIds_PersistsJunction()
    {
        var run = Guid.NewGuid().ToString("N").Substring(0, 8);
        var projectId = $"proj-{run}";
        var iA = $"intent-{run}-a";
        var iB = $"intent-{run}-b";
        var agentId = $"draft-router-recon-{run}";

        await SeedProjectAndIntentsAsync(projectId, new Dictionary<string, string>
        {
            [iA] = $"action_a_{run}",
            [iB] = $"action_b_{run}",
        });
        var client = ClientForProject(projectId);

        // Router passa pelo painel de aprovação: cria → submit (PendingApproval)
        // → approve via /agent-approvals — o approve dispara a reconciliação.
        var create = await client.PostAsJsonAsync(
            "/api/aihub/agent-drafts",
            BuildRouterDraftPayload(agentId, new[] { iA, iB }));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var submit = await client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{agentId}/submit",
            new { changeReason = "Publicação inicial do router com 2 intents." });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var approve = await client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{agentId}/approve",
            new { changeReason = "Aprovado pelo time de governança." });
        approve.StatusCode.Should().Be(HttpStatusCode.Created);

        var junction = await ReadJunctionAsync(agentId);
        junction.Should().BeEquivalentTo(new[] { iA, iB },
            "approve do draft Router precisa reconciliar a junction agent_router_intents — " +
            "sem isso o agente publicado fica órfão de intents");
    }

    [Fact]
    public async Task ApproveEditDraft_RouterAddingIntents_PersistsNewSetInJunction()
    {
        // Cobre o cenário reportado: edit-draft que adiciona intents novas a um
        // Router já publicado precisa propagar o set completo pra junction.
        var run = Guid.NewGuid().ToString("N").Substring(0, 8);
        var projectId = $"proj-{run}";
        var iA = $"intent-{run}-a";
        var iB = $"intent-{run}-b";
        var iC = $"intent-{run}-c";
        var agentId = $"draft-router-recon-edit-{run}";

        await SeedProjectAndIntentsAsync(projectId, new Dictionary<string, string>
        {
            [iA] = $"action_a_{run}",
            [iB] = $"action_b_{run}",
            [iC] = $"action_c_{run}",
        });
        var client = ClientForProject(projectId);

        // Publica primeiro com [iA].
        await client.PostAsJsonAsync(
            "/api/aihub/agent-drafts",
            BuildRouterDraftPayload(agentId, new[] { iA }));
        await client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{agentId}/submit",
            new { changeReason = "Publicação inicial com 1 intent." });
        var approveInitial = await client.PostAsJsonAsync(
            $"/api/aihub/agent-approvals/{agentId}/approve",
            new { changeReason = "Aprovação inicial." });
        approveInitial.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadJunctionAsync(agentId)).Should().BeEquivalentTo(new[] { iA });

        // Fork edit-draft e atualiza pra [iA, iB, iC].
        var fork = await client.PostAsync($"/api/aihub/agents/{agentId}/edit-draft", content: null);
        fork.StatusCode.Should().Be(HttpStatusCode.Created);
        var draftAfterFork = await fork.Content.ReadFromJsonAsync<JsonElement>();
        var updatedAt = draftAfterFork.GetProperty("updatedAt").GetString();

        var update = await client.PutAsJsonAsync(
            $"/api/aihub/agent-drafts/{agentId}",
            new
            {
                expectedUpdatedAt = updatedAt,
                payload = MergeRouterIntentIds(
                    draftAfterFork.GetProperty("payload"),
                    new[] { iA, iB, iC }),
            });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var submit = await client.PostAsJsonAsync(
            $"/api/aihub/agent-drafts/{agentId}/submit",
            new { changeReason = "Adicionei duas novas intents (iB, iC) ao router." });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        // Edit cosmético do classifier (ex.: só Description/Metadata) auto-aprova
        // no submit; comportamental fica pra fila e exige approve explícito.
        // Em ambos os caminhos a reconciliação roda — o teste só precisa cobrir o
        // ramo correto sem assumir qual classe a mudança caiu.
        var submitBody = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var autoApproved = submitBody.GetProperty("autoApproved").GetBoolean();
        if (!autoApproved)
        {
            var approve = await client.PostAsJsonAsync(
                $"/api/aihub/agent-approvals/{agentId}/approve",
                new { changeReason = "Aprovação da edição com novas intents." });
            approve.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var junction = await ReadJunctionAsync(agentId);
        junction.Should().BeEquivalentTo(new[] { iA, iB, iC },
            "approve do edit-draft precisa reconciliar a junction com o set novo de intents");
    }

    private static object MergeRouterIntentIds(JsonElement payload, IReadOnlyList<string> intentIds)
    {
        // Preserva todos os campos do payload do fork e sobrescreve só
        // routerIntentIds; mantém o jsonb intacto pra o backend não rejeitar
        // o PUT (a UI faz o mesmo deep-spread).
        using var doc = JsonDocument.Parse(payload.GetRawText());
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
        }
        dict["routerIntentIds"] = intentIds;
        return dict;
    }
}
