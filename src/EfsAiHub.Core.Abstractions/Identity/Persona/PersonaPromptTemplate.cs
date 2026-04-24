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

    // F9: coluna UpdatedBy foi deprecada no domínio. Actor canônico vive em
    // admin_audit_log (via IAdminAuditLogger). Coluna no DB fica pra drop
    // numa release subsequente após confirmar zero read no app (migration
    // db/migration_persona_templates_drop_updatedby.sql preparada, não
    // aplicada).

    /// <summary>
    /// Aponta pra <see cref="PersonaPromptTemplateVersion.VersionId"/> ativa
    /// em <c>aihub.persona_prompt_template_versions</c>. F5 — cada UPDATE
    /// via API cria nova version + move esse ponteiro na mesma transação.
    /// Nullable em rows pré-F5 até backfill migrar.
    /// </summary>
    public Guid? ActiveVersionId { get; set; }

    /// <summary>
    /// Scope global para um userType específico. Recomendação: sempre cadastrar
    /// <c>global:cliente</c> + <c>global:admin</c> em produção pra nenhum fluxo
    /// cair no fallback null.
    /// </summary>
    public static string GlobalScope(string userType) => $"global:{userType}";

    /// <summary>Override por agente + userType.</summary>
    public static string AgentScope(string agentId, string userType)
        => $"agent:{agentId}:{userType}";

    // F4: Scopes project-aware — override mais específico que o global e por
    // agente. ProjectId foi escolhido (ADR 003) em vez de TenantId paralelo,
    // porque Project já é o boundary de isolamento no repo. Cadeia completa
    // no composer:
    //
    //   1. project:{projectId}:agent:{agentId}:{userType}  (mais específico)
    //   2. project:{projectId}:{userType}
    //   3. agent:{agentId}:{userType}
    //   4. global:{userType}
    //   5. null (persona fica sem bloco)

    /// <summary>Scope global por project — override do <c>global:{userType}</c>.</summary>
    public static string ProjectGlobalScope(string projectId, string userType)
        => $"project:{projectId}:{userType}";

    /// <summary>Scope por agente + project + userType — nível mais específico.</summary>
    public static string ProjectAgentScope(string projectId, string agentId, string userType)
        => $"project:{projectId}:agent:{agentId}:{userType}";

    /// <summary>
    /// Identifica se o scope é project-aware. Usado por UI admin pra separar
    /// scopes globais de per-project.
    /// </summary>
    public static bool IsProjectScoped(string scope) =>
        scope.StartsWith("project:", StringComparison.Ordinal);
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
