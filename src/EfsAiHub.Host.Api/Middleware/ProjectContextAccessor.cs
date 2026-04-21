using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Implementação do <see cref="IProjectContextAccessor"/> usando AsyncLocal para que o
/// projectId definido pelo middleware HTTP flua corretamente para todos os escopos
/// criados internamente (ex: IDbContextFactory, background tasks da request).
/// </summary>
public sealed class ProjectContextAccessor : IProjectContextAccessor
{
    private static readonly AsyncLocal<ProjectContext?> _ambient = new();

    public ProjectContext Current
    {
        get => _ambient.Value ?? ProjectContext.Default;
        set => _ambient.Value = value;
    }
}
