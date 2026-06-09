namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Fonte do documento pra extração. União fechada via abstract record + nested
/// sealed records — ctor privado impede subtipos externos, garantindo
/// exhaustiveness no <c>switch</c> do extractor.
///
/// Substitui o par <c>Bytes?</c>/<c>SourceUri?</c> da interface inicial: o TL
/// apontou que aceitar ambos preenchidos era risco real de audit divergente
/// (sha256 dos bytes mas source_ref da URL).
///
/// Validações de payload (bytes vazios, URL malformada) ficam no extractor —
/// erros de input do usuário viram <see cref="ExtractionResult"/> Status="failed"
/// com ErrorCode apropriado, não exception.
/// </summary>
public abstract record ExtractionSource
{
    private ExtractionSource() { }

    /// <summary>
    /// Bytes já em memória. <see cref="SourceRef"/> é metadata-only para audit
    /// (URL de origem, path local, etc.) — NÃO é usada pra re-download. Quando
    /// preenchida, vai pro <c>source_ref</c> do job; null = "veio direto" (upload).
    /// </summary>
    public sealed record Bytes(byte[] Content, Uri? SourceRef = null) : ExtractionSource;

    /// <summary>
    /// URL remota que o extractor baixa antes da extração. O download serve pra
    /// computar SHA-256 e validar magic bytes localmente; em paralelo, a URL
    /// também pode ser passada pro Azure DI (preferida quando é Azure Blob com SAS).
    /// </summary>
    public sealed record Url(Uri Address) : ExtractionSource;
}
