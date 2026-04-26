namespace EfsAiHub.Core.Abstractions.Blocklist;

/// <summary>
/// Tipo de matching de um pattern. <c>BuiltIn</c> delega pra um handler dinâmico
/// (ex: <c>internal_tools</c> reflete o IFunctionToolRegistry em runtime).
/// </summary>
public enum BlocklistPatternType
{
    Literal,
    Regex,
    BuiltIn
}

/// <summary>
/// Ação aplicada quando um pattern bate. <c>Warn</c> apenas registra audit + métrica
/// sem afetar o conteúdo (uso: detecção em sombra antes de subir pra block).
/// </summary>
public enum BlocklistAction
{
    Block,
    Redact,
    Warn
}

/// <summary>
/// Validador pós-match. Reduz falsos positivos do regex aplicando checksum
/// específico do domínio (Mod11 = CPF/CNPJ, Luhn = cartão).
/// </summary>
public enum BlocklistValidator
{
    None,
    Mod11,
    Luhn
}

/// <summary>Fase do pipeline em que o scan rodou. Vai pro audit + counter.</summary>
public enum BlocklistPhase
{
    Input,
    Output
}

/// <summary>Grupo curado vindo de aihub.blocklist_pattern_groups.</summary>
public sealed record BlocklistPatternGroup(
    string Id,
    string Name,
    string? Description,
    int Version);

/// <summary>Pattern curado vindo de aihub.blocklist_patterns.</summary>
public sealed record BlocklistPattern(
    string Id,
    string GroupId,
    BlocklistPatternType Type,
    string Pattern,
    BlocklistValidator Validator,
    bool WholeWord,
    bool CaseSensitive,
    BlocklistAction DefaultAction,
    bool Enabled,
    int Version);

/// <summary>
/// Snapshot completo do catálogo lido em uma chamada do repositório.
/// Versão é o max de todas as versões individuais — usado pelo cache pra detectar mudança.
/// </summary>
public sealed record BlocklistCatalogSnapshot(
    IReadOnlyList<BlocklistPatternGroup> Groups,
    IReadOnlyList<BlocklistPattern> Patterns,
    int Version);

/// <summary>
/// Pattern resolvido após aplicar override do projeto. <c>EffectiveAction</c>
/// pode diferir de <c>Source.DefaultAction</c> se houver <c>action_override</c> no grupo.
/// <c>Category</c> é o ID do grupo do catálogo (ou "CUSTOM" para patterns do projeto)
/// — vai pro envelope HTTP e métricas.
/// </summary>
public sealed record EffectivePattern(
    BlocklistPattern Source,
    string Category,
    BlocklistAction EffectiveAction);
