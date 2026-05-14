namespace EfsAiHub.Core.Abstractions.Users;

/// <summary>
/// Vínculo entre um usuário e um projeto. Linhas vivem em aihub.user_projects.
/// Admin não usa essa tabela — bypass total via User.IsAdmin. Non-admin só
/// enxerga projetos cujo Id aparece aqui pra ele.
/// </summary>
public sealed class UserProject
{
    public required Guid UserId { get; init; }
    public required string ProjectId { get; init; }
    public required DateTime GrantedAt { get; init; }
    /// <summary>ExternalUserId do admin que criou o vínculo (audit trail leve).</summary>
    public string? GrantedBy { get; init; }
}
