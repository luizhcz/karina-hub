namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Converte um JSON Schema em qualquer dialect (Draft 4/6/7/2019-09/2020-12)
/// pro shape canônico interno: OpenAI strict mode 2020-12. Determinístico —
/// mesmo input produz mesmo <see cref="CanonicalSchema.CanonicalJson"/> e
/// <see cref="CanonicalSchema.CanonicalHash"/>. Save-time only; runtime
/// consome só o canônico persistido.
/// </summary>
public interface ISchemaNormalizer
{
    CanonicalSchema Normalize(string rawJson, SchemaRole role);
}
