using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Infra.Observability;

/// <summary>
/// Extension methods pra popular spans com contexto recorrente
/// (persona, tenant, hash de userId). Centralizado aqui pra evitar
/// que cada call site reimplemente regras de redaction/hashing.
///
/// Regra LGPD: IDs brutos de usuário NUNCA vão em tags OTel — só
/// hash SHA-256 truncado (8 bytes hex = 16 chars).
/// </summary>
public static class ActivityExtensions
{
    private const int HashLength = 16; // 8 bytes = 64 bits = baixa colisão, log leve

    /// <summary>
    /// Adiciona tags de persona no span. No-op se <paramref name="activity"/>
    /// for null (span não está sendo coletado) ou <paramref name="persona"/>
    /// for null/Anonymous.
    /// </summary>
    public static Activity? SetPersonaTags(this Activity? activity, UserPersona? persona)
    {
        if (activity is null || persona is null || persona.IsAnonymous)
            return activity;

        activity.SetTag("persona.user_type", persona.UserType);
        activity.SetTag("persona.user_id_hash", HashUserId(persona.UserId));

        // Tags específicas por subtipo — são os atributos que mais
        // influenciam comportamento do prompt (ver PersonaPromptComposer).
        switch (persona)
        {
            case ClientPersona c:
                if (!string.IsNullOrEmpty(c.BusinessSegment))
                    activity.SetTag("persona.segment", c.BusinessSegment);
                if (!string.IsNullOrEmpty(c.SuitabilityLevel))
                    activity.SetTag("persona.suitability", c.SuitabilityLevel);
                break;

            case AdminPersona a:
                if (!string.IsNullOrEmpty(a.PartnerType))
                    activity.SetTag("persona.partner_type", a.PartnerType);
                if (a.IsWm)
                    activity.SetTag("persona.wm", true);
                break;
        }

        return activity;
    }

    /// <summary>
    /// Hash SHA-256 truncado (16 chars hex) do userId pra compliance LGPD
    /// em tags de observabilidade. Determinístico — mesmo userId sempre
    /// gera mesmo hash, útil pra correlacionar spans.
    /// </summary>
    public static string HashUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "anonymous";
        var bytes = Encoding.UTF8.GetBytes(userId);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, HashLength / 2).ToLowerInvariant();
    }
}
