namespace EfsAiHub.Tests.Integration.Agents;

/// <summary>
/// Verifica que recursos criados em um projeto não são visíveis de outro projeto.
/// Cobre agentes, workflows e skills — os três domínios com HasQueryFilter por ProjectId.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class ProjectIsolationTests(IntegrationWebApplicationFactory factory)
{
    // ── Agentes ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agent_CriadoEmProjetoA_NaoVisivelEmProjetoB()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var projetoB = Guid.NewGuid().ToString("N");
        var agentId = $"agent-iso-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);
        var clientB = factory.CreateClient().WithProject(projetoB);

        var post = await clientA.PostAsJsonAsync("/api/agents", new
        {
            id = agentId,
            name = "Isolation Test Agent",
            model = new { deploymentName = "gpt-4o" }
        });
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var getFromB = await clientB.GetAsync($"/api/agents/{agentId}");

        getFromB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Agent_CriadoEmProjetoA_VisivelNoMesmoProjetoA()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var agentId = $"agent-visible-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);

        await clientA.PostAsJsonAsync("/api/agents", new
        {
            id = agentId,
            name = "Visible Agent",
            model = new { deploymentName = "gpt-4o" }
        });

        var getFromA = await clientA.GetAsync($"/api/agents/{agentId}");

        getFromA.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AgentList_RetornaApenasRecursosDoProjetoCorreto()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var projetoB = Guid.NewGuid().ToString("N");
        var agentIdA = $"agent-lista-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);
        var clientB = factory.CreateClient().WithProject(projetoB);

        await clientA.PostAsJsonAsync("/api/agents", new
        {
            id = agentIdA,
            name = "List Isolation Agent",
            model = new { deploymentName = "gpt-4o" }
        });

        var listFromB = await clientB.GetAsync("/api/agents");
        var body = await listFromB.Content.ReadFromJsonAsync<JsonElement>();

        listFromB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Nenhum agente do projeto A deve aparecer na lista do projeto B
        var ids = body.EnumerateArray()
            .Select(e => e.TryGetProperty("id", out var p) ? p.GetString() : null)
            .ToList();
        ids.Should().NotContain(agentIdA);
    }

    // ── Workflows ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Workflow_CriadoEmProjetoA_NaoVisivelEmProjetoB()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var projetoB = Guid.NewGuid().ToString("N");
        var workflowId = $"wf-iso-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);
        var clientB = factory.CreateClient().WithProject(projetoB);

        // Criar agente no projeto A (workflows exigem ao menos um agente)
        var agentId = $"agent-wf-{Guid.NewGuid():N}";
        await clientA.PostAsJsonAsync("/api/agents", new
        {
            id = agentId,
            name = "WF Isolation Agent",
            model = new { deploymentName = "gpt-4o" }
        });

        var post = await clientA.PostAsJsonAsync("/api/workflows", new
        {
            id = workflowId,
            name = "Isolation Test Workflow",
            orchestrationMode = "Sequential",
            agents = new[] { new { agentId } }
        });
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var getFromB = await clientB.GetAsync($"/api/workflows/{workflowId}");

        getFromB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Skill_CriadaEmProjetoA_NaoVisivelEmProjetoB()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var projetoB = Guid.NewGuid().ToString("N");
        var skillId = $"skill-iso-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);
        var clientB = factory.CreateClient().WithProject(projetoB);

        var put = await clientA.PutAsJsonAsync($"/api/skills/{skillId}", new
        {
            id = skillId,
            name = "Isolation Test Skill",
            description = "Cross-project isolation test"
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var getFromB = await clientB.GetAsync($"/api/skills/{skillId}");

        getFromB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Skill_CriadaEmProjetoA_VisivelNoMesmoProjetoA()
    {
        var projetoA = Guid.NewGuid().ToString("N");
        var skillId = $"skill-visible-{Guid.NewGuid():N}";

        var clientA = factory.CreateClient().WithProject(projetoA);

        await clientA.PutAsJsonAsync($"/api/skills/{skillId}", new
        {
            id = skillId,
            name = "Visible Skill",
            description = "Same-project visibility test"
        });

        var getFromA = await clientA.GetAsync($"/api/skills/{skillId}");

        getFromA.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DefaultProjectGuard (gate ativo) ──────────────────────────────────────

    [Fact]
    public async Task ProjetoDefault_SemProjectHeader_ComGateAtivo_Retorna403()
    {
        var client = factory.CreateClientWithAdminGate("test-admin-999");
        // Sem x-efs-project-id → ProjectId resolve para "default" → 403 para não-admin

        var response = await client.GetAsync("/api/agents");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProjetoDefault_ComAdminAccount_ComGateAtivo_Passa()
    {
        var client = factory.CreateClientWithAdminGate("test-admin-999")
            .WithAdminAccount("test-admin-999");

        var response = await client.GetAsync("/api/agents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
