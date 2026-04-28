namespace EfsAiHub.Core.Abstractions.Secrets;

public sealed record SecretContext(
    SecretScope Scope,
    string? ProjectId = null,
    string? Provider = null,
    string? Label = null)
{
    public static SecretContext Global(string? label = null)
        => new(SecretScope.Global, Label: label);

    public static SecretContext Project(string projectId, string? provider = null, string? label = null)
        => new(SecretScope.Project, projectId, provider, label);

    public static SecretContext Agent(string projectId, string? agentLabel = null)
        => new(SecretScope.Agent, projectId, Label: agentLabel);

    public static SecretContext Foundry(string projectId)
        => new(SecretScope.Foundry, projectId);
}
