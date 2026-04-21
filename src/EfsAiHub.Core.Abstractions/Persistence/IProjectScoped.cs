namespace EfsAiHub.Core.Abstractions.Persistence;

/// <summary>
/// Marker interface para entidades que pertencem a um projeto.
/// Usado pelo DbContext para aplicar HasQueryFilter automático por ProjectId.
/// </summary>
public interface IProjectScoped
{
    string ProjectId { get; }
}
