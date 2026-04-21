namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Contexto de usuário resolvido por requisição. UserType classifica a origem
/// da identificação (ex.: "cliente", "assessor") preservando a semântica
/// atual dos headers x-efs-account / x-efs-user-profile-id.
/// </summary>
public sealed class UserContext
{
    public string UserId { get; }
    public string UserType { get; }

    public UserContext(string userId, string userType)
    {
        UserId = userId;
        UserType = userType;
    }
}
