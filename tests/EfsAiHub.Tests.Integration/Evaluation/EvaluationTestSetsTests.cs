namespace EfsAiHub.Tests.Integration.Evaluation;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class EvaluationTestSetsTests(IntegrationWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>
    /// Cliente per-projeto — `x-efs-project-id` é necessário pra <c>HasQueryFilter</c>
    /// do DbContext deixar o testset criado fora do projeto "default" passar.
    /// </summary>
    private HttpClient ClientFor(string projectId)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Remove("x-efs-project-id");
        c.DefaultRequestHeaders.Add("x-efs-project-id", projectId);
        return c;
    }

    [Fact]
    public async Task Create_Project_TestSet_Retorna_201()
    {
        var projectId = await EnsureProjectAsync("test-eval-1");
        var body = new { name = "MyTestSet", description = "Cobertura básica", visibility = "project" };

        var client = ClientFor(projectId);
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/evaluation-test-sets", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<TestSetDto>();
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("MyTestSet");
        dto.Visibility.Should().Be("project");
    }

    [Fact]
    public async Task Create_Visibility_Global_Retorna_403()
    {
        var projectId = await EnsureProjectAsync("test-eval-2");
        var body = new { name = "Global", visibility = "global" };

        var client = ClientFor(projectId);
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/evaluation-test-sets", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publish_Version_Sem_Cases_Retorna_400()
    {
        var projectId = await EnsureProjectAsync("test-eval-3");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var response = await client.PostAsJsonAsync(
            $"/api/evaluation-test-sets/{ts.Id}/versions",
            new { cases = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Publish_Version_Idempotente_Mesmo_ContentHash()
    {
        var projectId = await EnsureProjectAsync("test-eval-4");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var body = new
        {
            cases = new[]
            {
                new { input = "Q1", expectedOutput = "A1", weight = 1.0 },
                new { input = "Q2", expectedOutput = "A2", weight = 1.0 }
            }
        };

        var first = await client.PostAsJsonAsync($"/api/evaluation-test-sets/{ts.Id}/versions", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var v1 = await first.Content.ReadFromJsonAsync<TestSetVersionDto>();

        // Publish do mesmo conteúdo: ContentHash bate, repo retorna a mesma version (no-op).
        var second = await client.PostAsJsonAsync($"/api/evaluation-test-sets/{ts.Id}/versions", body);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var v2 = await second.Content.ReadFromJsonAsync<TestSetVersionDto>();

        v2!.TestSetVersionId.Should().Be(v1!.TestSetVersionId);
        v2.ContentHash.Should().Be(v1.ContentHash);
    }

    [Fact]
    public async Task ImportCsv_HappyPath_Cria_Version_E_Cases()
    {
        var projectId = await EnsureProjectAsync("test-eval-5");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var csv = "input,expectedOutput,tags,weight\n"
                + "What's the weather?,Sunny,weather|easy,1.0\n"
                + "Calc 2+2,4,math,1.0\n";
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "test.csv");
        content.Add(new StringContent("Initial import"), "changeReason");

        var response = await client.PostAsync($"/api/evaluation-test-sets/{ts.Id}/versions/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var version = await response.Content.ReadFromJsonAsync<TestSetVersionDto>();
        version.Should().NotBeNull();

        // Lista cases via endpoint dedicado.
        var cases = await client.GetFromJsonAsync<List<TestCaseDto>>(
            $"/api/evaluation-test-sets/versions/{version!.TestSetVersionId}/cases");
        cases.Should().HaveCount(2);
        cases![0].Input.Should().Be("What's the weather?");
    }

    [Fact]
    public async Task ImportCsv_CSV_Vazio_Retorna_400()
    {
        var projectId = await EnsureProjectAsync("test-eval-6");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("input\n"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "empty.csv");

        var response = await client.PostAsync($"/api/evaluation-test-sets/{ts.Id}/versions/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_Lista_Header_E_Versions()
    {
        var projectId = await EnsureProjectAsync("test-eval-7");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var publish = await client.PostAsJsonAsync($"/api/evaluation-test-sets/{ts.Id}/versions",
            new { cases = new[] { new { input = "Q", expectedOutput = "A", weight = 1.0 } } });
        publish.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.GetAsync($"/api/evaluation-test-sets/{ts.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<TestSetWithVersionsDto>();
        detail!.TestSet.Id.Should().Be(ts.Id);
        detail.Versions.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task UpdateVersionStatus_Para_Deprecated_Retorna_204()
    {
        var projectId = await EnsureProjectAsync("test-eval-8");
        var (client, ts) = await CreateTestSetWithClientAsync(projectId);

        var publish = await client.PostAsJsonAsync($"/api/evaluation-test-sets/{ts.Id}/versions",
            new { cases = new[] { new { input = "Q", expectedOutput = "A", weight = 1.0 } } });
        publish.StatusCode.Should().Be(HttpStatusCode.Created);
        var v = await publish.Content.ReadFromJsonAsync<TestSetVersionDto>();

        var response = await client.PutAsJsonAsync(
            $"/api/evaluation-test-sets/{ts.Id}/versions/{v!.TestSetVersionId}/status",
            new { status = "Deprecated" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verifica via GET.
        var detail = await client.GetFromJsonAsync<TestSetWithVersionsDto>($"/api/evaluation-test-sets/{ts.Id}");
        detail!.Versions.Single(x => x.TestSetVersionId == v.TestSetVersionId).Status.Should().Be("Deprecated");
    }

    [Fact]
    public async Task TenantGuard_GetById_Outro_Projeto_Retorna_404()
    {
        // Cria testset no projeto A.
        var projectA = await EnsureProjectAsync("test-eval-tenant-a");
        var (clientA, ts) = await CreateTestSetWithClientAsync(projectA);

        // Tenta acessar do projeto B — deve retornar 404 (defesa em profundidade
        // contra enumeration; não distingue "não existe" de "existe noutro tenant").
        var projectB = await EnsureProjectAsync("test-eval-tenant-b");
        var clientB = ClientFor(projectB);
        var response = await clientB.GetAsync($"/api/evaluation-test-sets/{ts.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantGuard_PublishVersion_Outro_Projeto_Retorna_404()
    {
        var projectA = await EnsureProjectAsync("test-eval-tenant-a-pub");
        var (_, ts) = await CreateTestSetWithClientAsync(projectA);

        var projectB = await EnsureProjectAsync("test-eval-tenant-b-pub");
        var clientB = ClientFor(projectB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/evaluation-test-sets/{ts.Id}/versions",
            new { cases = new[] { new { input = "Q", expectedOutput = "A", weight = 1.0 } } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Copy_Cross_Project_Cria_Novo_TestSet()
    {
        var sourceProject = await EnsureProjectAsync("test-eval-src");
        var targetProject = await EnsureProjectAsync("test-eval-tgt");
        var (client, ts) = await CreateTestSetWithClientAsync(sourceProject);
        var publish = await client.PostAsJsonAsync($"/api/evaluation-test-sets/{ts.Id}/versions",
            new { cases = new[] { new { input = "Q", expectedOutput = "A", weight = 1.0 } } });
        publish.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.PostAsync(
            $"/api/evaluation-test-sets/{ts.Id}/copy?targetProject={targetProject}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var clone = await response.Content.ReadFromJsonAsync<TestSetDto>();
        clone!.Id.Should().NotBe(ts.Id);
        clone.ProjectId.Should().Be(targetProject);
    }

    private async Task<(HttpClient Client, TestSetDto TestSet)> CreateTestSetWithClientAsync(string projectId, string? name = null)
    {
        var client = ClientFor(projectId);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/evaluation-test-sets",
            new { name = name ?? $"TS-{Guid.NewGuid():N}", visibility = "project" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await response.Content.ReadFromJsonAsync<TestSetDto>())!;
        return (client, dto);
    }

    private async Task<string> EnsureProjectAsync(string projectId)
    {
        // Cria o projeto idempotentemente — endpoint do ProjectsController.
        var existing = await _client.GetAsync($"/api/projects/{projectId}");
        if (existing.StatusCode == HttpStatusCode.OK) return projectId;

        var body = new { id = projectId, name = projectId, tenantId = "test-tenant" };
        var response = await _client.PostAsJsonAsync("/api/projects", body);
        // 201 Created ou 409 Conflict (race entre testes paralelos) ambos OK.
        if (response.StatusCode != HttpStatusCode.Created && response.StatusCode != HttpStatusCode.Conflict)
            response.EnsureSuccessStatusCode();
        return projectId;
    }

    private sealed record TestSetDto(
        string Id,
        string ProjectId,
        string Name,
        string? Description,
        string Visibility,
        string? CurrentVersionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? CreatedBy);

    private sealed record TestSetVersionDto(
        string TestSetVersionId,
        string TestSetId,
        int Revision,
        string Status,
        string ContentHash,
        DateTime CreatedAt,
        string? CreatedBy,
        string? ChangeReason);

    private sealed record TestSetWithVersionsDto(
        TestSetDto TestSet,
        List<TestSetVersionDto> Versions);

    private sealed record TestCaseDto(
        string CaseId,
        int Index,
        string Input,
        string? ExpectedOutput,
        JsonElement? ExpectedToolCalls,
        List<string> Tags,
        double Weight,
        DateTime CreatedAt);
}
