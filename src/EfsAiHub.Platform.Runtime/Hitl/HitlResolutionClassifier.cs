using System.Text.Json;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Classifica respostas de HITL como aprovação ou rejeição.
/// Ponto único de parsing — usado pelo AgUiApprovalMiddleware, ConfirmBoletaFunction
/// e qualquer outra function tool que solicite aprovação humana.
///
/// Suporta dois formatos de payload:
///   1. JSON estruturado: {"approved": bool, "reason"?: string}
///   2. String raw: comparada contra lista fechada de termos de rejeição.
/// </summary>
public static class HitlResolutionClassifier
{
    private static readonly HashSet<string> RejectionTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "rejected",
        "rejeitar",
        "rejeitado",
        "cancelar",
        "cancelado",
        "cancelled",
        "cancel",
        "não",
        "nao",
        "no",
        "timeout",
        "expired"
    };

    /// <summary>
    /// Determina se a resposta do humano é uma aprovação.
    /// Tenta parsing JSON primeiro; fallback para string matching.
    /// </summary>
    public static bool IsApproved(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Tenta parsing JSON estruturado: {"approved": true/false}
        if (TryParseJson(content, out var approved))
            return approved;

        // Fallback: string raw contra lista fechada de rejeições
        return !RejectionTerms.Contains(content.Trim());
    }

    /// <summary>
    /// Inverso de <see cref="IsApproved"/> — conveniência para function tools.
    /// </summary>
    public static bool IsRejected(string? content) => !IsApproved(content);

    private static bool TryParseJson(string content, out bool approved)
    {
        approved = false;

        if (!content.StartsWith('{'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("approved", out var prop) && prop.ValueKind == JsonValueKind.True)
            {
                approved = true;
                return true;
            }
            if (prop.ValueKind == JsonValueKind.False)
            {
                approved = false;
                return true;
            }
        }
        catch (JsonException)
        {
            // Não é JSON válido — fallback para string matching
        }

        return false;
    }
}
