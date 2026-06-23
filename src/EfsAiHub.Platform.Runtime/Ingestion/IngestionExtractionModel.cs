namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Modelo do Document Intelligence usado na extração de PDF na ingestão, escolhido
/// pelo cliente via metadata <c>"model"</c> do request (default <see cref="Default"/>).
///
/// O modelo ACOPLA formato de saída e extensão do objeto no S3 (restrição do Azure DI):
/// <list type="bullet">
///   <item><c>prebuilt-layout</c> → markdown (<c>.md</c>) — preserva estrutura
///         (tabelas/títulos), melhor pro LLM, mais caro.</item>
///   <item><c>prebuilt-read</c> → texto puro (<c>.txt</c>) — mais barato. É o default.</item>
/// </list>
/// Não há combinação inválida: o modelo determina formato e extensão. TXT/MD recebidos
/// não passam por DI, então este modelo não se aplica a eles.
/// </summary>
public static class IngestionExtractionModel
{
    /// <summary>Chave no metadata do request que carrega o nome do modelo.</summary>
    public const string MetadataKey = "model";

    public const string Layout = "prebuilt-layout";
    public const string Read = "prebuilt-read";

    /// <summary>Default quando o metadata não traz o modelo.</summary>
    public const string Default = Read;

    public static bool IsValid(string? model) =>
        string.Equals(model, Layout, StringComparison.OrdinalIgnoreCase)
        || string.Equals(model, Read, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Valor cru do metadata <c>"model"</c> (chave case-insensitive), ou <c>null</c> se
    /// ausente. Usado pelo controller pra validar antes de enfileirar.
    /// </summary>
    public static string? FindRawValue(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return null;
        foreach (var kv in metadata)
            if (string.Equals(kv.Key, MetadataKey, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    /// <summary>
    /// Resolve o modelo canônico a partir do metadata (chave/valor case-insensitive).
    /// Valor ausente ou inválido cai no <see cref="Default"/> — a validação estrita
    /// (rejeitar valor inválido) é feita no controller na entrada.
    /// </summary>
    public static string Resolve(IReadOnlyDictionary<string, string>? metadata)
    {
        var raw = FindRawValue(metadata);
        return string.Equals(raw, Layout, StringComparison.OrdinalIgnoreCase) ? Layout : Default;
    }

    /// <summary><c>"markdown"</c> pra layout, <c>"text"</c> pra read.</summary>
    public static string OutputFormat(string model) =>
        string.Equals(model, Layout, StringComparison.OrdinalIgnoreCase) ? "markdown" : "text";

    /// <summary>Extensão do objeto extraído no S3: <c>"md"</c> pra layout, <c>"txt"</c> pra read.</summary>
    public static string FileExtension(string model) =>
        string.Equals(model, Layout, StringComparison.OrdinalIgnoreCase) ? "md" : "txt";

    /// <summary>Content-Type do objeto no S3, coerente com o formato.</summary>
    public static string ContentType(string model) =>
        string.Equals(model, Layout, StringComparison.OrdinalIgnoreCase)
            ? "text/markdown; charset=utf-8"
            : "text/plain; charset=utf-8";
}
