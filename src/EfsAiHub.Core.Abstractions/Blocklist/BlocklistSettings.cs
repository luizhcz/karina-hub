namespace EfsAiHub.Core.Abstractions.Blocklist;

/// <summary>
/// Override de um grupo curado dentro do projeto.
/// <para>
/// <c>DisabledPatterns</c> é killswitch granular: lista de IDs (ex: "pii.cpf") que ficam
/// desligados mesmo com o grupo enabled. Resolve o caso "PII está OK mas o pattern X dá
/// falso positivo no nosso domínio".
/// </para>
/// </summary>
public sealed record BlocklistGroupOverride(
    bool Enabled = true,
    BlocklistAction? ActionOverride = null,
    IReadOnlyList<string>? DisabledPatterns = null);

/// <summary>
/// Pattern específico do projeto, não vindo do catálogo. Usado pra termos
/// internos (codenames, nomes de produtos) que não cabem em grupo curado.
/// </summary>
public sealed record BlocklistCustomPattern(
    string Id,
    BlocklistPatternType Type,
    string Pattern,
    BlocklistAction Action = BlocklistAction.Block,
    bool WholeWord = true,
    bool CaseSensitive = false);

/// <summary>
/// Configuração de blocklist do projeto. Vive em ProjectSettings.Blocklist (JSONB).
/// <para>
/// Resolução em runtime: <c>effective = (catálogo onde Groups[gid].Enabled
/// e pattern.Id NOT IN DisabledPatterns) + CustomPatterns</c>. Cada pattern carrega
/// a action efetiva (DefaultAction do catálogo OU ActionOverride do grupo).
/// </para>
/// </summary>
public sealed record BlocklistSettings(
    bool Enabled = false,
    bool ScanInput = true,
    bool ScanOutput = true,
    string Replacement = "[REDACTED]",
    bool AuditBlocks = true,
    IReadOnlyDictionary<string, BlocklistGroupOverride>? Groups = null,
    IReadOnlyList<BlocklistCustomPattern>? CustomPatterns = null)
{
    /// <summary>Default conservador: blocklist desligado, scan em ambas fases quando ligar.</summary>
    public static BlocklistSettings Default { get; } = new();
}
