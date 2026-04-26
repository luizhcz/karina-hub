using EfsAiHub.Infra.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Messaging;

/// <summary>
/// Cobre o canal blocklist_changed end-to-end:
/// - Trigger SQL dispara pg_notify após INSERT/UPDATE/DELETE em blocklist_patterns/groups.
/// - PgNotifyDispatcher.SubscribeBlocklistChanged recebe notification.
/// - Handler dispara fire-and-forget Task.Run.
/// - Subscription Dispose remove handler.
///
/// Não testa reconnect-loop completo (pesado e cobrindo PgEventBusLifecycleTests).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class BlocklistChangedNotifyTests(IntegrationWebApplicationFactory factory)
{
    private PgNotifyDispatcher Dispatcher =>
        factory.Services.GetRequiredService<PgNotifyDispatcher>();

    private string Conn => factory.ConnectionString;

    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

    private async Task ResetCatalogAsync()
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE aihub.blocklist_patterns, aihub.blocklist_pattern_groups CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(50);
        }
        return predicate();
    }

    [Fact]
    public async Task TriggerInsert_EmGruposDoCatalogo_DisparaNotifyParaSubscriber()
    {
        await ResetCatalogAsync();

        var received = 0;
        using var subscription = Dispatcher.SubscribeBlocklistChanged(() =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        // Aguarda subscription estar registrada antes do INSERT (best-effort).
        await Task.Delay(100);

        await ExecAsync(
            "INSERT INTO aihub.blocklist_pattern_groups (\"Id\", \"Name\") VALUES ('G_TEST', 'Test Group');");

        var got = await WaitForAsync(() => received >= 1, ReceiveTimeout);
        got.Should().BeTrue("trigger SQL deve disparar pg_notify após INSERT em blocklist_pattern_groups");
        received.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task TriggerUpdate_EmPatterns_DisparaNotify()
    {
        await ResetCatalogAsync();
        await ExecAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name") VALUES ('G_UPDT', 'g');
            INSERT INTO aihub.blocklist_patterns ("Id", "GroupId", "Type", "Pattern", "DefaultAction")
            VALUES ('p.updt', 'G_UPDT', 'literal', 'foo', 'block');
            """);

        var received = 0;
        using var subscription = Dispatcher.SubscribeBlocklistChanged(() =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        await ExecAsync("UPDATE aihub.blocklist_patterns SET \"Pattern\" = 'bar' WHERE \"Id\" = 'p.updt';");

        var got = await WaitForAsync(() => received >= 1, ReceiveTimeout);
        got.Should().BeTrue("trigger SQL deve disparar pg_notify após UPDATE em blocklist_patterns");
    }

    [Fact]
    public async Task TriggerDelete_DisparaNotify()
    {
        await ResetCatalogAsync();
        await ExecAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name") VALUES ('G_DEL', 'g');
            INSERT INTO aihub.blocklist_patterns ("Id", "GroupId", "Type", "Pattern", "DefaultAction")
            VALUES ('p.del', 'G_DEL', 'literal', 'foo', 'block');
            """);

        var received = 0;
        using var subscription = Dispatcher.SubscribeBlocklistChanged(() =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        await ExecAsync("DELETE FROM aihub.blocklist_patterns WHERE \"Id\" = 'p.del';");

        var got = await WaitForAsync(() => received >= 1, ReceiveTimeout);
        got.Should().BeTrue("trigger SQL deve disparar pg_notify após DELETE em blocklist_patterns");
    }

    [Fact]
    public async Task MultiplosSubscribers_RecebemNotify()
    {
        await ResetCatalogAsync();

        var s1 = 0; var s2 = 0;
        using var sub1 = Dispatcher.SubscribeBlocklistChanged(() => { Interlocked.Increment(ref s1); return Task.CompletedTask; });
        using var sub2 = Dispatcher.SubscribeBlocklistChanged(() => { Interlocked.Increment(ref s2); return Task.CompletedTask; });

        await Task.Delay(100);

        await ExecAsync(
            "INSERT INTO aihub.blocklist_pattern_groups (\"Id\", \"Name\") VALUES ('G_MULTI', 'g');");

        var got = await WaitForAsync(() => s1 >= 1 && s2 >= 1, ReceiveTimeout);
        got.Should().BeTrue("ambos subscribers devem receber");
    }

    [Fact]
    public async Task SubscriptionDispose_HandlerNaoEhMaisChamado()
    {
        await ResetCatalogAsync();

        var received = 0;
        var subscription = Dispatcher.SubscribeBlocklistChanged(() =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        await Task.Delay(100);
        subscription.Dispose();

        // Após dispose, mais um INSERT não deve incrementar received.
        await ExecAsync(
            "INSERT INTO aihub.blocklist_pattern_groups (\"Id\", \"Name\") VALUES ('G_DISPOSED', 'g');");

        // Aguarda janela razoável pra confirmar que NÃO chega notification.
        await Task.Delay(500);
        received.Should().Be(0, "handler removido pelo Dispose não deve mais ser invocado");
    }

    [Fact]
    public async Task StatementBatch_GeraApenasUmNotifyPorComando()
    {
        // Trigger é FOR EACH STATEMENT (não FOR EACH ROW). 1 INSERT com múltiplas rows
        // deve gerar EXATAMENTE 1 NOTIFY (otimização: app re-fetch o catálogo completo).
        await ResetCatalogAsync();

        var received = 0;
        using var subscription = Dispatcher.SubscribeBlocklistChanged(() =>
        {
            Interlocked.Increment(ref received);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        // 1 statement, 3 rows.
        await ExecAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name") VALUES
                ('G_BATCH_1', 'g'),
                ('G_BATCH_2', 'g'),
                ('G_BATCH_3', 'g');
            """);

        var got = await WaitForAsync(() => received >= 1, ReceiveTimeout);
        got.Should().BeTrue("ao menos 1 notify deve chegar");

        // Janela maior pra garantir que não chegam mais notifications.
        await Task.Delay(500);
        received.Should().Be(1, "FOR EACH STATEMENT garante 1 notify por comando, mesmo com batch");
    }
}
