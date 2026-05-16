using Npgsql;

namespace EfsAiHub.Tests.Integration.Middleware;

/// <summary>
/// Regression: rotas marcadas em UserProvisioning:SkipPathPrefixes (default:
/// AG-UI stream) não devem inflar aihub.users com identidades de callers
/// externos. Rotas da plataforma continuam fazendo upsert normal.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class UserProvisioningSkipTests(IntegrationWebApplicationFactory factory)
{
    private readonly IntegrationWebApplicationFactory _factory = factory;

    [Fact]
    public async Task PostAgUiStream_NaoCriaLinhaEmAihubUsers()
    {
        var externalUserId = $"ext-{Guid.NewGuid():N}";
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/aihub/chat/ag-ui/stream");
        request.Headers.Add("x-efs-account", externalUserId);
        request.Content = JsonContent.Create(new
        {
            messages = new[] { new { role = "user", content = "Olá" } }
        });

        // Não importa o status final do stream — só queremos que o middleware
        // já tenha rodado. SendAsync com ResponseHeadersRead garante isso.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        var count = await CountUsersAsync(externalUserId);
        count.Should().Be(0, "AG-UI é caller externo — não deve provisionar em aihub.users");
    }

    [Fact]
    public async Task GetMe_CriaLinhaEmAihubUsers()
    {
        var externalUserId = $"ext-{Guid.NewGuid():N}";
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-efs-account", externalUserId);

        // /me não está em SkipPathPrefixes — middleware deve fazer upsert
        // normal. Status do response não importa pro objetivo do teste.
        using var response = await client.GetAsync("/api/aihub/me");

        var count = await CountUsersAsync(externalUserId);
        count.Should().Be(1, "rota da plataforma deve provisionar em aihub.users");
    }

    private async Task<int> CountUsersAsync(string externalUserId)
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """SELECT COUNT(*) FROM aihub.users WHERE "ExternalUserId" = @id;""", conn);
        cmd.Parameters.AddWithValue("id", externalUserId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
