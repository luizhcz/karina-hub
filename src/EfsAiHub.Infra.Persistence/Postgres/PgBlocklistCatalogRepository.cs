using EfsAiHub.Core.Abstractions.Blocklist;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Lê o catálogo curado de blocklist (aihub.blocklist_pattern_groups + aihub.blocklist_patterns).
/// Read-only do ponto de vista do app — atualizações via db/seeds.sql + apply.sh pelo DBA.
/// Chamado pelo BlocklistCache (L1+L2) em runtime; invalidação cross-pod via NOTIFY 'blocklist_changed'.
/// </summary>
public sealed class PgBlocklistCatalogRepository : IBlocklistCatalogRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PgBlocklistCatalogRepository(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public async Task<BlocklistCatalogSnapshot> LoadAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        var groups = await LoadGroupsAsync(conn, ct);
        var patterns = await LoadPatternsAsync(conn, ct);

        var maxVersion = 0;
        foreach (var g in groups) if (g.Version > maxVersion) maxVersion = g.Version;
        foreach (var p in patterns) if (p.Version > maxVersion) maxVersion = p.Version;

        return new BlocklistCatalogSnapshot(groups, patterns, maxVersion);
    }

    private static async Task<IReadOnlyList<BlocklistPatternGroup>> LoadGroupsAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "Name", "Description", "Version"
            FROM aihub.blocklist_pattern_groups
            ORDER BY "Id"
            """;

        var results = new List<BlocklistPatternGroup>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BlocklistPatternGroup(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                Version: reader.GetInt32(3)));
        }
        return results;
    }

    private static async Task<IReadOnlyList<BlocklistPattern>> LoadPatternsAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        // Filtro Enabled = true alinha com o index parcial ix_blocklist_patterns_group.
        // Patterns desligados no catálogo nem chegam ao engine — killswitch global do DBA.
        cmd.CommandText = """
            SELECT "Id", "GroupId", "Type", "Pattern", "ValidatorFn",
                   "WholeWord", "CaseSensitive", "DefaultAction", "Enabled", "Version"
            FROM aihub.blocklist_patterns
            WHERE "Enabled" = TRUE
            ORDER BY "GroupId", "Id"
            """;

        var results = new List<BlocklistPattern>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BlocklistPattern(
                Id: reader.GetString(0),
                GroupId: reader.GetString(1),
                Type: ParseType(reader.GetString(2)),
                Pattern: reader.GetString(3),
                Validator: ParseValidator(reader.IsDBNull(4) ? null : reader.GetString(4)),
                WholeWord: reader.GetBoolean(5),
                CaseSensitive: reader.GetBoolean(6),
                DefaultAction: ParseAction(reader.GetString(7)),
                Enabled: reader.GetBoolean(8),
                Version: reader.GetInt32(9)));
        }
        return results;
    }

    // SQL CHECK constraints garantem que o valor é um dos enums conhecidos —
    // Enum.Parse com ignoreCase aceita o lowercase do banco e converte pro PascalCase do .NET.
    private static BlocklistPatternType ParseType(string raw)
        => Enum.Parse<BlocklistPatternType>(raw, ignoreCase: true);

    private static BlocklistAction ParseAction(string raw)
        => Enum.Parse<BlocklistAction>(raw, ignoreCase: true);

    private static BlocklistValidator ParseValidator(string? raw)
        => string.IsNullOrEmpty(raw)
            ? BlocklistValidator.None
            : Enum.Parse<BlocklistValidator>(raw, ignoreCase: true);
}
