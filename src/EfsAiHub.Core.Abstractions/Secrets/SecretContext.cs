namespace EfsAiHub.Core.Abstractions.Secrets;

public sealed record SecretContext(
    SecretScope Scope,
    string? ProjectId = null,
    string? Provider = null,
    string? Label = null,
    string? OriginProjectId = null)
{
    public static SecretContext Global(string? label = null)
        => new(SecretScope.Global, Label: label);

    public static SecretContext Project(string projectId, string? provider = null, string? label = null)
        => new(SecretScope.Project, projectId, provider, label);

    public static SecretContext Agent(string projectId, string? agentLabel = null)
        => new(SecretScope.Agent, projectId, Label: agentLabel);

    public static SecretContext Foundry(string projectId)
        => new(SecretScope.Foundry, projectId);

    /// <summary>
    /// Cria contexto cross-project: o caller (request scope) é
    /// <paramref name="callerProjectId"/>, mas o secret deve ser resolvido
    /// no contexto do <paramref name="ownerProjectId"/> (project dono do agent global).
    /// Resolver e cache devem segregar por OriginProjectId pra evitar leak entre projetos.
    /// </summary>
    public static SecretContext CrossProject(
        string callerProjectId, string ownerProjectId, string? provider = null, string? label = null)
        => new(SecretScope.Project, callerProjectId, provider, label, ownerProjectId);

    /// <summary>
    /// Project usado para resolver o secret de fato. Quando <see cref="OriginProjectId"/>
    /// é setado e diferente de <see cref="ProjectId"/>, retorna OriginProjectId (owner do agent global).
    /// Senão retorna ProjectId (caller normal).
    /// </summary>
    public string? EffectiveProjectId =>
        !string.IsNullOrEmpty(OriginProjectId) ? OriginProjectId : ProjectId;
}
