using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Abstractions.Secrets;

namespace EfsAiHub.Host.Api.Identity.Secrets;

/// <summary>
/// Coleta refs <c>secret://aws/...</c> a partir de project settings:
/// <list type="bullet">
///   <item><c>Settings.Evaluation.Foundry.ApiKeyRef</c>.</item>
///   <item><c>LlmConfig.Credentials[*].ApiKey</c>.</item>
/// </list>
/// Falha graciosa: se o repo lança, devolve set vazio e o preloader loga.
/// </summary>
public sealed class ProjectSecretReferenceSource : ISecretReferenceSource
{
    private readonly IProjectRepository _projects;
    private readonly ILogger<ProjectSecretReferenceSource> _logger;

    public ProjectSecretReferenceSource(
        IProjectRepository projects,
        ILogger<ProjectSecretReferenceSource> logger)
    {
        _projects = projects;
        _logger = logger;
    }

    public string Name => "projects";

    public async Task<IReadOnlyCollection<string>> CollectAsync(CancellationToken ct)
    {
        var refs = new List<string>();
        IReadOnlyList<Project> all;
        try
        {
            all = await _projects.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ProjectSecretReferenceSource] Falha listando projects — refs per-project não pré-carregadas.");
            return Array.Empty<string>();
        }

        foreach (var p in all)
        {
            var foundryRef = p.Settings.Evaluation?.Foundry?.ApiKeyRef;
            if (!string.IsNullOrWhiteSpace(foundryRef)) refs.Add(foundryRef);

            if (p.LlmConfig?.Credentials is { Count: > 0 } creds)
            {
                foreach (var (_, providerCreds) in creds)
                {
                    if (!string.IsNullOrWhiteSpace(providerCreds.ApiKey))
                        refs.Add(providerCreds.ApiKey);
                }
            }
        }
        return refs;
    }
}
