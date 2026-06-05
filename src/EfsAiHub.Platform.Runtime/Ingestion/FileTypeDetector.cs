using System.Text;

namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Tipos aceitos pelo pipeline de ingestão. Qualquer outro shape é rejeitado
/// com <see cref="DetectedFileType.Unsupported"/>.
/// </summary>
public enum DetectedFileType
{
    Unsupported = 0,
    Pdf = 1,
    Text = 2,
    Markdown = 3
}

/// <summary>
/// Detecta o tipo do arquivo a partir do conteúdo bruto + hints (Content-Type,
/// extensão da URL). Não confia somente em hints — validação por bytes é a
/// fonte de verdade, hints só desempatam TXT vs MD.
///
/// Estratégia:
/// <list type="bullet">
///   <item>Magic bytes <c>%PDF-</c> → <see cref="DetectedFileType.Pdf"/>.</item>
///   <item>UTF-8/ASCII decodificável + sem bytes de controle binários → texto.
///         Diferencia TXT vs MD via hint (Content-Type ou extensão); ambíguo
///         vira Markdown (superset visual e tooling-friendly).</item>
///   <item>Qualquer outro → <see cref="DetectedFileType.Unsupported"/>.</item>
/// </list>
/// </summary>
public static class FileTypeDetector
{
    // Bytes de controle que não são whitespace válido — presença forte de binário.
    // Permite: HT(0x09), LF(0x0A), VT(0x0B), FF(0x0C), CR(0x0D).
    // Inclui 0x7F (DEL) — primeiro byte do header ELF é 0x7F e queremos
    // rejeitar binários executáveis que casariam UTF-8 puro nos próximos bytes.
    private static bool IsBinaryControl(byte b) =>
        b switch
        {
            0x00 => true,
            <= 0x08 => true,
            >= 0x0E and <= 0x1F => true,
            0x7F => true,
            _ => false,
        };

    public static DetectedFileType Detect(
        ReadOnlySpan<byte> content,
        string? contentTypeHint = null,
        string? urlPathHint = null)
    {
        if (content.Length == 0) return DetectedFileType.Unsupported;

        // PDF: magic bytes %PDF- nos primeiros 5 bytes. Definitivo.
        if (content.Length >= 5
            && content[0] == 0x25  // %
            && content[1] == 0x50  // P
            && content[2] == 0x44  // D
            && content[3] == 0x46  // F
            && content[4] == 0x2D) // -
        {
            return DetectedFileType.Pdf;
        }

        // Heurística texto: scan dos primeiros N bytes — se houver byte de
        // controle binário, rejeita; senão tenta decodificar como UTF-8.
        var sampleLen = Math.Min(content.Length, 8192);
        for (var i = 0; i < sampleLen; i++)
        {
            if (IsBinaryControl(content[i])) return DetectedFileType.Unsupported;
        }

        try
        {
            // Decode estrito (strict=true) rejeita sequências UTF-8 inválidas.
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            _ = strictUtf8.GetString(content[..sampleLen]);
        }
        catch (DecoderFallbackException)
        {
            return DetectedFileType.Unsupported;
        }

        return ResolveTextSubtype(contentTypeHint, urlPathHint);
    }

    private static DetectedFileType ResolveTextSubtype(string? contentTypeHint, string? urlPathHint)
    {
        // Content-Type tem prioridade — vem do servidor que produziu o arquivo.
        if (!string.IsNullOrWhiteSpace(contentTypeHint))
        {
            var ct = contentTypeHint.ToLowerInvariant();
            if (ct.Contains("markdown")) return DetectedFileType.Markdown;
            if (ct.StartsWith("text/plain")) return DetectedFileType.Text;
            // Outro text/* (text/html, text/csv, etc.) — não suportamos. Rejeita.
            if (ct.StartsWith("text/")) return DetectedFileType.Unsupported;
        }

        // Extensão da URL como fallback.
        if (!string.IsNullOrWhiteSpace(urlPathHint))
        {
            var path = urlPathHint.ToLowerInvariant();
            if (path.EndsWith(".md") || path.EndsWith(".markdown")) return DetectedFileType.Markdown;
            if (path.EndsWith(".txt")) return DetectedFileType.Text;
        }

        // Sem hint TXT/MD — arquivo que passou o filtro de bytes binários ainda
        // pode ser qualquer texto desconhecido (HTML cru, CSV, source code).
        // Rejeitamos por segurança em vez de assumir Markdown silenciosamente.
        return DetectedFileType.Unsupported;
    }
}
