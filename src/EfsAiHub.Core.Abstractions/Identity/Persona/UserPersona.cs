using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Base abstrata de persona. Duas materializações concretas: <see cref="ClientPersona"/>
/// e <see cref="AdminPersona"/>. Campos reais vivem nos tipos concretos porque não
/// há overlap significativo entre cliente e admin (ex: cliente tem suitability, admin
/// tem institutions+booleans de capacidade).
///
/// A base carrega apenas o axis de identificação (UserId, UserType) e o contrato
/// de substituição de placeholders usado pelo <see cref="PersonaTemplateRenderer"/>
/// — cada subtipo declara seu próprio mapeamento placeholder → valor, evitando que
/// o renderer precise fazer pattern matching.
///
/// <para>
/// Polimorfismo JSON: discriminador <c>$personaType</c> (nome neutro pra não colidir
/// com o campo <c>userType</c> que já existe no payload). Garante round-trip correto
/// quando serializamos como base abstrata (ex: endpoints admin).
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$personaType")]
[JsonDerivedType(typeof(ClientPersona), "cliente")]
[JsonDerivedType(typeof(AdminPersona), "admin")]
public abstract record UserPersona(string UserId, string UserType)
{
    /// <summary>
    /// true quando nenhum campo personalizável foi resolvido — composer não emite
    /// seção de persona no system message.
    /// </summary>
    public abstract bool IsAnonymous { get; }

    /// <summary>
    /// Resolve o valor de um placeholder <c>{{key}}</c>. Retorna null para keys
    /// desconhecidas — renderer preserva o literal no output pra expor typos.
    ///
    /// Convenções de formatação:
    ///  - string opcional: null vira "" (não "null" literal)
    ///  - bool: "sim"/"não" (explícito pro LLM, menos ambíguo que true/false)
    ///  - lista: CSV com ", " (empty list → "")
    /// </summary>
    public abstract string? GetPlaceholderValue(string key);
}

/// <summary>
/// Persona de cliente final (investidor). Resolvida da API externa
/// <c>GET {ClientEndpoint}/{userId}</c>. Todos os campos opcionais — API pode
/// vir parcial. Se nenhum resolver, <see cref="IsAnonymous"/> é true.
/// </summary>
public sealed record ClientPersona(
    string UserId,
    string? ClientName,
    string? SuitabilityLevel,
    string? SuitabilityDescription,
    string? BusinessSegment,
    string? Country,
    bool IsOffshore) : UserPersona(UserId, "cliente")
{
    public override bool IsAnonymous =>
        ClientName is null
        && SuitabilityLevel is null
        && SuitabilityDescription is null
        && BusinessSegment is null
        && Country is null
        && !IsOffshore;

    public override string? GetPlaceholderValue(string key) => key switch
    {
        "client_name" => ClientName ?? "",
        "suitability_level" => SuitabilityLevel ?? "",
        "suitability_description" => SuitabilityDescription ?? "",
        "business_segment" => BusinessSegment ?? "",
        "country" => Country ?? "",
        "is_offshore" => IsOffshore ? "sim" : "não",
        "user_type" => UserType,
        _ => null,
    };

    public static ClientPersona Anonymous(string userId) =>
        new(userId, null, null, null, null, null, false);
}

/// <summary>
/// Persona de admin (assessor/gestor/consultor/padrão). Resolvida da API externa
/// <c>GET {AdminEndpoint}/{userId}</c>. <c>PartnerType</c> carrega o sub-role
/// (DEFAULT | CONSULTOR | GESTOR | ADVISORS). <c>Segments</c> e <c>Institutions</c>
/// podem ser vazios — normalizados para lista vazia pelo provider, nunca null.
/// </summary>
public sealed record AdminPersona(
    string UserId,
    string? Username,
    string? PartnerType,
    IReadOnlyList<string> Segments,
    IReadOnlyList<string> Institutions,
    bool IsInternal,
    bool IsWm,
    bool IsMaster,
    bool IsBroker) : UserPersona(UserId, "admin")
{
    public override bool IsAnonymous =>
        Username is null
        && PartnerType is null
        && Segments.Count == 0
        && Institutions.Count == 0
        && !IsInternal
        && !IsWm
        && !IsMaster
        && !IsBroker;

    public override string? GetPlaceholderValue(string key) => key switch
    {
        "username" => Username ?? "",
        "partner_type" => PartnerType ?? "",
        "segments" => string.Join(", ", Segments),
        "institutions" => string.Join(", ", Institutions),
        "is_internal" => IsInternal ? "sim" : "não",
        "is_wm" => IsWm ? "sim" : "não",
        "is_master" => IsMaster ? "sim" : "não",
        "is_broker" => IsBroker ? "sim" : "não",
        "user_type" => UserType,
        _ => null,
    };

    public static AdminPersona Anonymous(string userId) =>
        new(userId, null, null, Array.Empty<string>(), Array.Empty<string>(), false, false, false, false);
}

/// <summary>
/// Factory estático — resolve o Anonymous correto conforme o userType do caller.
/// Centraliza a decisão pra não vazar o switch em todo callsite do provider/cache.
/// </summary>
public static class UserPersonaFactory
{
    public const string ClienteUserType = "cliente";
    public const string AdminUserType = "admin";

    /// <summary>
    /// Quando <paramref name="userType"/> não bate nem com <see cref="ClienteUserType"/>
    /// nem com <see cref="AdminUserType"/>, cai em <see cref="ClientPersona.Anonymous"/>
    /// como fallback conservador — e dispara um Activity event
    /// <c>persona.unknown_user_type</c> no span corrente (se existir). Em runtime
    /// com OTel coletando, admin vê sinal de bug upstream (header malformado,
    /// valor inesperado vindo da persona API, typo em config).
    /// </summary>
    public static UserPersona Anonymous(string userId, string userType)
    {
        switch (userType)
        {
            case ClienteUserType: return ClientPersona.Anonymous(userId);
            case AdminUserType: return AdminPersona.Anonymous(userId);
            default:
                System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent(
                    "persona.unknown_user_type",
                    tags: new System.Diagnostics.ActivityTagsCollection
                    {
                        ["persona.user_type"] = userType ?? "",
                    }));
                return ClientPersona.Anonymous(userId);
        }
    }
}
