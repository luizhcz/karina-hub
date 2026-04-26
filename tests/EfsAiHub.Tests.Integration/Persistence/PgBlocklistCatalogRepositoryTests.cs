using EfsAiHub.Core.Abstractions.Blocklist;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Tests.Integration.Persistence;

/// <summary>
/// Testa o PgBlocklistCatalogRepository contra Postgres real:
/// - Queries SELECT batem com schema.
/// - Enum.Parse(ignoreCase) lê valores lowercase do banco.
/// - Filter Enabled=true exclui patterns desligados.
/// - Snapshot.Version = max(group.Version, pattern.Version).
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class PgBlocklistCatalogRepositoryTests(IntegrationWebApplicationFactory factory)
{
    private IBlocklistCatalogRepository Repo =>
        factory.Services.GetRequiredService<IBlocklistCatalogRepository>();

    private string Conn => factory.ConnectionString;

    private async Task ResetCatalogAsync()
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE aihub.blocklist_patterns, aihub.blocklist_pattern_groups CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LoadAllAsync_BancoVazio_RetornaSnapshotComListasVaziasEVersionZero()
    {
        await ResetCatalogAsync();

        var snapshot = await Repo.LoadAllAsync();

        snapshot.Groups.Should().BeEmpty();
        snapshot.Patterns.Should().BeEmpty();
        snapshot.Version.Should().Be(0);
    }

    [Fact]
    public async Task LoadAllAsync_ComGruposEPatterns_MapeiaCamposCorretamente()
    {
        await ResetCatalogAsync();
        await SeedAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name", "Description", "Version") VALUES
                ('PII', 'Dados Pessoais', 'CPF, CNPJ', 3);
            INSERT INTO aihub.blocklist_patterns
                ("Id", "GroupId", "Type", "Pattern", "ValidatorFn", "WholeWord", "CaseSensitive", "DefaultAction", "Enabled", "Version") VALUES
                ('pii.cpf', 'PII', 'regex', '\d{11}', 'mod11', TRUE, FALSE, 'block', TRUE, 5);
            """);

        var snapshot = await Repo.LoadAllAsync();

        snapshot.Groups.Should().ContainSingle();
        var group = snapshot.Groups[0];
        group.Id.Should().Be("PII");
        group.Name.Should().Be("Dados Pessoais");
        group.Description.Should().Be("CPF, CNPJ");
        group.Version.Should().Be(3);

        snapshot.Patterns.Should().ContainSingle();
        var p = snapshot.Patterns[0];
        p.Id.Should().Be("pii.cpf");
        p.GroupId.Should().Be("PII");
        p.Type.Should().Be(BlocklistPatternType.Regex);
        p.Pattern.Should().Be(@"\d{11}");
        p.Validator.Should().Be(BlocklistValidator.Mod11);
        p.WholeWord.Should().BeTrue();
        p.CaseSensitive.Should().BeFalse();
        p.DefaultAction.Should().Be(BlocklistAction.Block);
        p.Enabled.Should().BeTrue();
        p.Version.Should().Be(5);
    }

    [Fact]
    public async Task LoadAllAsync_EnumLowercaseDoBanco_ParseiaParaPascalCaseDoTipo()
    {
        // Confirma que Enum.Parse(ignoreCase: true) lida com TODOS os enums:
        // Type=literal/regex/builtin, ValidatorFn=mod11/luhn, DefaultAction=block/redact/warn.
        await ResetCatalogAsync();
        await SeedAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name") VALUES ('G1', 'g'), ('G2', 'g'), ('G3', 'g');
            INSERT INTO aihub.blocklist_patterns
                ("Id", "GroupId", "Type", "Pattern", "ValidatorFn", "DefaultAction") VALUES
                ('p.literal', 'G1', 'literal', '-----BEGIN', NULL, 'block'),
                ('p.regex',   'G2', 'regex',   'foo',        'luhn', 'redact'),
                ('p.builtin', 'G3', 'builtin', 'internal_tools', NULL, 'warn');
            """);

        var snapshot = await Repo.LoadAllAsync();
        snapshot.Patterns.Should().HaveCount(3);

        var byId = snapshot.Patterns.ToDictionary(p => p.Id);
        byId["p.literal"].Type.Should().Be(BlocklistPatternType.Literal);
        byId["p.literal"].Validator.Should().Be(BlocklistValidator.None);
        byId["p.literal"].DefaultAction.Should().Be(BlocklistAction.Block);
        byId["p.regex"].Type.Should().Be(BlocklistPatternType.Regex);
        byId["p.regex"].Validator.Should().Be(BlocklistValidator.Luhn);
        byId["p.regex"].DefaultAction.Should().Be(BlocklistAction.Redact);
        byId["p.builtin"].Type.Should().Be(BlocklistPatternType.BuiltIn);
        byId["p.builtin"].Validator.Should().Be(BlocklistValidator.None);
        byId["p.builtin"].DefaultAction.Should().Be(BlocklistAction.Warn);
    }

    [Fact]
    public async Task LoadAllAsync_PatternComEnabledFalse_NaoApareceNoSnapshot()
    {
        // Filter Enabled=true alinha com o index parcial. Patterns desligados pelo DBA
        // funcionam como killswitch global.
        await ResetCatalogAsync();
        await SeedAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name") VALUES ('G', 'g');
            INSERT INTO aihub.blocklist_patterns
                ("Id", "GroupId", "Type", "Pattern", "DefaultAction", "Enabled") VALUES
                ('p.on',  'G', 'literal', 'on',  'block', TRUE),
                ('p.off', 'G', 'literal', 'off', 'block', FALSE);
            """);

        var snapshot = await Repo.LoadAllAsync();

        snapshot.Patterns.Should().ContainSingle(p => p.Id == "p.on");
        snapshot.Patterns.Should().NotContain(p => p.Id == "p.off");
    }

    [Fact]
    public async Task LoadAllAsync_VersionDoSnapshot_EhMaxDeGroupsEPatterns()
    {
        await ResetCatalogAsync();
        await SeedAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name", "Version") VALUES
                ('G1', 'g', 7),
                ('G2', 'g', 2);
            INSERT INTO aihub.blocklist_patterns
                ("Id", "GroupId", "Type", "Pattern", "DefaultAction", "Version") VALUES
                ('p1', 'G1', 'literal', 'a', 'block', 4),
                ('p2', 'G2', 'literal', 'b', 'block', 12);
            """);

        var snapshot = await Repo.LoadAllAsync();

        // Max entre 7, 2, 4, 12 = 12.
        snapshot.Version.Should().Be(12);
    }

    [Fact]
    public async Task LoadAllAsync_DescriptionNull_MapeadoComoNull()
    {
        await ResetCatalogAsync();
        await SeedAsync("""
            INSERT INTO aihub.blocklist_pattern_groups ("Id", "Name", "Description") VALUES
                ('G', 'Sem Descrição', NULL);
            """);

        var snapshot = await Repo.LoadAllAsync();
        snapshot.Groups[0].Description.Should().BeNull();
    }
}
