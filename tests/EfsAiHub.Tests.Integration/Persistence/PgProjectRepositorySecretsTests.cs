using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class PgProjectRepositorySecretsTests(IntegrationWebApplicationFactory factory)
{
    private IProjectRepository Repo => factory.Services.GetRequiredService<IProjectRepository>();

    private static Project MakeProject(string id, ProjectLlmConfig llmConfig) => new()
    {
        Id          = id,
        Name        = $"test-{id}",
        TenantId    = "default",
        Description = null,
        Settings    = new ProjectSettings(),
        LlmConfig   = llmConfig,
        Budget      = null,
        CreatedAt   = DateTime.UtcNow,
        UpdatedAt   = DateTime.UtcNow
    };

    [Fact]
    public async Task SecretRef_PreservedAcrossRoundTrip()
    {
        var project = MakeProject($"proj-secref-{Guid.NewGuid():N}", new ProjectLlmConfig
        {
            Credentials = new Dictionary<string, ProviderCredentials>
            {
                ["OPENAI"] = new() { ApiKey = "secret://aws/efs-ai-hub-test-openai", Endpoint = "https://e.example" }
            }
        });

        await Repo.CreateAsync(project);
        var fetched = await Repo.GetByIdAsync(project.Id);

        fetched.Should().NotBeNull();
        fetched!.LlmConfig!.Credentials["OPENAI"].ApiKey.Should().Be("secret://aws/efs-ai-hub-test-openai");
        fetched.LlmConfig.Credentials["OPENAI"].Endpoint.Should().Be("https://e.example");
    }

    [Fact]
    public async Task LiteralLegacy_DpapiRoundTrip_StillWorks()
    {
        var project = MakeProject($"proj-dpapi-{Guid.NewGuid():N}", new ProjectLlmConfig
        {
            Credentials = new Dictionary<string, ProviderCredentials>
            {
                ["OPENAI"] = new() { ApiKey = "sk-test-literal-legacy", Endpoint = "https://e.example" }
            }
        });

        await Repo.CreateAsync(project);
        var fetched = await Repo.GetByIdAsync(project.Id);

        fetched.Should().NotBeNull();
        fetched!.LlmConfig!.Credentials["OPENAI"].ApiKey.Should().Be("sk-test-literal-legacy");
    }

    [Fact]
    public async Task Coexistence_TwoProjects_SecretRefAndDpapi_BothResolveCorrectly()
    {
        var newProject = MakeProject($"proj-new-{Guid.NewGuid():N}", new ProjectLlmConfig
        {
            Credentials = new Dictionary<string, ProviderCredentials>
            {
                ["OPENAI"] = new() { ApiKey = "secret://aws/efs-co-new" }
            }
        });
        var legacyProject = MakeProject($"proj-legacy-{Guid.NewGuid():N}", new ProjectLlmConfig
        {
            Credentials = new Dictionary<string, ProviderCredentials>
            {
                ["OPENAI"] = new() { ApiKey = "sk-co-legacy" }
            }
        });

        await Repo.CreateAsync(newProject);
        await Repo.CreateAsync(legacyProject);

        var newFetched = await Repo.GetByIdAsync(newProject.Id);
        var legacyFetched = await Repo.GetByIdAsync(legacyProject.Id);

        newFetched!.LlmConfig!.Credentials["OPENAI"].ApiKey.Should().Be("secret://aws/efs-co-new");
        legacyFetched!.LlmConfig!.Credentials["OPENAI"].ApiKey.Should().Be("sk-co-legacy");
    }

    [Fact]
    public async Task SecretRef_PersistedInJsonbField_NotInApiKeyCipher()
    {
        var project = MakeProject($"proj-jsonb-{Guid.NewGuid():N}", new ProjectLlmConfig
        {
            Credentials = new Dictionary<string, ProviderCredentials>
            {
                ["OPENAI"] = new() { ApiKey = "secret://aws/efs-jsonb-check" }
            }
        });

        await Repo.CreateAsync(project);

        var connStr = factory.ConnectionString;
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT llm_config::text FROM aihub.projects WHERE id = @id";
        cmd.Parameters.AddWithValue("id", project.Id);
        var raw = (string?)await cmd.ExecuteScalarAsync();

        raw.Should().NotBeNull();
        raw.Should().Contain("secretRef");
        raw.Should().Contain("efs-jsonb-check");
        raw.Should().NotContain("apiKeyCipher\":\"C");
    }
}
