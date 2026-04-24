namespace EfsAiHub.Core.Agents.McpServers;

/// <summary>
/// Registro de um servidor MCP (Model Context Protocol) conhecido pela plataforma.
/// Agents passam a referenciar MCPs pelo <see cref="Id"/> deste registro — os campos
/// <c>ServerLabel</c>, <c>ServerUrl</c>, <c>AllowedTools</c> e <c>Headers</c> são
/// resolvidos em runtime pelo provider LLM (resolução live, não snapshotada).
///
/// Escopo: project-scoped via <see cref="ProjectId"/>. A consulta é filtrada pelo
/// <c>IProjectContextAccessor</c> (HasQueryFilter no DbContext).
///
/// Secrets em <see cref="Headers"/> (ex: <c>Authorization</c>) ficam plaintext na
/// coluna <c>Data</c> JSONB — mesmo tratamento de <c>projects.llm_config.ApiKey</c> hoje.
/// Encriptação via Data Protection está no backlog.
/// </summary>
public sealed class McpServer
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Label enviado ao provider (Azure Foundry). Ex: "filesystem", "github".</summary>
    public required string ServerLabel { get; init; }

    /// <summary>URL absoluta (http/https) do endpoint MCP. Validado como Uri no controller.</summary>
    public required string ServerUrl { get; init; }

    /// <summary>Whitelist de tools do MCP que os agents podem invocar. Obrigatório ao menos 1 item.</summary>
    public List<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// Headers customizados enviados em cada request ao MCP (ex: <c>Authorization: Bearer ...</c>).
    /// Valores são passados ao provider como-está.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = [];

    /// <summary>"never" | "always" — política de aprovação HITL. Default: "never".</summary>
    public string RequireApproval { get; init; } = "never";

    public string ProjectId { get; set; } = "default";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
