using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Persistence;

/// <summary>
/// Round-trip de <see cref="Actor"/> via repo + EF Core. Garante que o value converter
/// custom (string lowercase ↔ enum) salva e relê sem regressão.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class ChatMessageActorRoundTripTests(IntegrationWebApplicationFactory factory)
{
    private IChatMessageRepository Repo =>
        factory.Services.GetRequiredService<IChatMessageRepository>();

    private NpgsqlDataSource DataSource =>
        factory.Services.GetRequiredKeyedService<NpgsqlDataSource>("general");

    [Fact]
    public async Task SaveAndList_ActorRobot_PersistEntreSessions()
    {
        var convId = $"conv-{Guid.NewGuid():N}";
        var msg = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = convId,
            Role = "user",
            Content = "{\"saldo\":12480}",
            Actor = Actor.Robot
        };

        await Repo.SaveAsync(msg);

        // Releitura via repo (DbContext novo internamente) — confirma value converter.
        var fetched = await Repo.ListAsync(convId);

        fetched.Should().HaveCount(1);
        fetched[0].Actor.Should().Be(Actor.Robot);
        fetched[0].Role.Should().Be("user");
        fetched[0].Content.Should().Be("{\"saldo\":12480}");
    }

    [Fact]
    public async Task SaveAndList_ActorHumanDefault_QuandoOmitido()
    {
        var convId = $"conv-{Guid.NewGuid():N}";
        var msg = new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = convId,
            Role = "user",
            Content = "olá"
            // Actor não setado → default Human
        };

        await Repo.SaveAsync(msg);

        var fetched = await Repo.ListAsync(convId);

        fetched.Should().HaveCount(1);
        fetched[0].Actor.Should().Be(Actor.Human);
    }

    [Fact]
    public async Task SaveActor_ColunaArmazenadaEmLowercase()
    {
        // Confirma que o value converter usa string lowercase (queries no psql ficam legíveis,
        // e bate com o DEFAULT 'human' do schema). Lê coluna direto via Npgsql, sem EF.
        var convId = $"conv-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid().ToString("N");
        await Repo.SaveAsync(new ChatMessage
        {
            MessageId = messageId,
            ConversationId = convId,
            Role = "user",
            Content = "x",
            Actor = Actor.Robot
        });

        await using var conn = await DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Actor\" FROM aihub.chat_messages WHERE \"MessageId\" = @id",
            conn);
        cmd.Parameters.AddWithValue("id", messageId);
        var actorRaw = (string?)await cmd.ExecuteScalarAsync();

        actorRaw.Should().Be("robot");
    }
}
