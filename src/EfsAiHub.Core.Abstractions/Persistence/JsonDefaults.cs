using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Abstractions.Persistence;

/// <summary>
/// Opções centralizadas de serialização JSON usadas por todos os backends de persistência.
/// Evita instâncias duplicadas de JsonSerializerOptions entre repositórios.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Opções para serializar/deserializar entidades de domínio armazenadas como JSON.
    /// Inclui JsonStringEnumConverter e preserva casing de propriedades.
    ///
    /// Encoder UnsafeRelaxedJsonEscaping: evita escape de caracteres não-ASCII
    /// (ex: "ã" não vira "ã"). Os textos em português permanecem legíveis
    /// nos payloads persistidos (workflow_event_audit, etc.) e nas respostas
    /// da API. "Unsafe" refere-se apenas a output direto em HTML sem escape —
    /// JSON puro é seguro.
    /// </summary>
    public static readonly JsonSerializerOptions Domain = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Opções para deserializar input externo/HTTP onde o casing de propriedades pode variar.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
