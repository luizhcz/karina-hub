namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Resultado da normalização. <see cref="CanonicalJson"/> é o que vai pro
/// banco e pro LLM — runtime nunca consome o original. <see cref="Warnings"/>
/// volta na response do save pra UI/auditoria. <see cref="CanonicalHash"/>
/// alimenta o <c>SchemaCache</c> sem precisar re-serializar a cada lookup.
/// </summary>
/// <param name="CanonicalJson">JSON Schema canônico (OpenAI strict mode 2020-12 compatible).</param>
/// <param name="SourceDialect">Dialect detectado do input (ex.: "draft-07", "2020-12", "unknown").</param>
/// <param name="Warnings">Lista de transformações lossy aplicadas durante a normalização.</param>
/// <param name="CanonicalHash">SHA256 hex do <see cref="CanonicalJson"/> pra cache lookup.</param>
public sealed record CanonicalSchema(
    string CanonicalJson,
    string SourceDialect,
    IReadOnlyList<NormalizationWarning> Warnings,
    string CanonicalHash);
