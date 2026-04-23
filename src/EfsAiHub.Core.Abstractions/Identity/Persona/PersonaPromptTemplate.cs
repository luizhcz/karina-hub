namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Template textual usado por <see cref="IPersonaPromptComposer"/> para compor
/// o bloco de persona no system message do agente. Persistido em
/// <c>aihub.persona_prompt_templates</c> (1 linha por <see cref="Scope"/>).
///
/// Update in-place: editar altera a mesma linha. Audit trail vive em
/// <c>admin_audit_log</c> (não há versionamento de linhas por decisão do MVP).
/// </summary>
public sealed class PersonaPromptTemplate
{
    public int Id { get; init; }

    /// <summary>
    /// Escopo de aplicação (sempre inclui userType como sufixo):
    ///   - <c>"global:cliente"</c> / <c>"global:admin"</c> — default por userType.
    ///   - <c>"agent:{agentId}:cliente"</c> / <c>"agent:{agentId}:admin"</c> — override
    ///     específico para um agente + userType.
    /// Cadeia de fallback do runtime: <c>agent:{id}:{userType}</c> → <c>global:{userType}</c>
    /// → null (persona entra sem bloco).
    /// </summary>
    public required string Scope { get; set; }

    /// <summary>Nome legível do template (exibido no admin UI).</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Template cru com placeholders <c>{{field}}</c>. Campos suportados
    /// dependem do userType — ver <see cref="PersonaPlaceholders.ForUserType"/>.
    /// Placeholder não reconhecido é deixado intocado (typos ficam visíveis).
    /// </summary>
    public required string Template { get; set; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Scope global para um userType específico. Recomendação: sempre cadastrar
    /// <c>global:cliente</c> + <c>global:admin</c> em produção pra nenhum fluxo
    /// cair no fallback null.
    /// </summary>
    public static string GlobalScope(string userType) => $"global:{userType}";

    /// <summary>Override por agente + userType.</summary>
    public static string AgentScope(string agentId, string userType)
        => $"agent:{agentId}:{userType}";
}

/// <summary>
/// Placeholders suportados pelo renderer. Diferem por userType — cliente
/// tem suitability/country, admin tem institutions/booleans de capacidade.
/// Centralizados pra que a UI admin liste no editor sem hardcoding.
/// </summary>
public static class PersonaPlaceholders
{
    // Comuns
    public const string UserType = "user_type";

    // Cliente
    public const string ClientName = "client_name";
    public const string SuitabilityLevel = "suitability_level";
    public const string SuitabilityDescription = "suitability_description";
    public const string BusinessSegment = "business_segment";
    public const string Country = "country";
    public const string IsOffshore = "is_offshore";

    // Admin
    public const string Username = "username";
    public const string PartnerType = "partner_type";
    public const string Segments = "segments";
    public const string Institutions = "institutions";
    public const string IsInternal = "is_internal";
    public const string IsWm = "is_wm";
    public const string IsMaster = "is_master";
    public const string IsBroker = "is_broker";

    public static readonly IReadOnlyList<string> ForClient = new[]
    {
        ClientName, SuitabilityLevel, SuitabilityDescription, BusinessSegment,
        Country, IsOffshore, UserType,
    };

    public static readonly IReadOnlyList<string> ForAdmin = new[]
    {
        Username, PartnerType, Segments, Institutions,
        IsInternal, IsWm, IsMaster, IsBroker, UserType,
    };

    public static IReadOnlyList<string> ForUserType(string userType) => userType switch
    {
        UserPersonaFactory.ClienteUserType => ForClient,
        UserPersonaFactory.AdminUserType => ForAdmin,
        _ => Array.Empty<string>(),
    };
}
