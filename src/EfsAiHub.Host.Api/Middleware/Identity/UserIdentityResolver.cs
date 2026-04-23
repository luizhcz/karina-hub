namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Resolve identidade do usuário a partir dos headers HTTP.
/// Suporta dois modos de identificação:
///   - x-efs-account → userId com userType "cliente"
///   - x-efs-user-profile-id → userId com userType "admin"
/// Exatamente um header deve estar presente.
///
/// Nota: "admin" cobre assessor, gestor, consultor e padrão — o sub-tipo
/// real vem no campo <c>partnerType</c> da <c>AdminPersona</c>.
/// </summary>
public class UserIdentityResolver
{
    public static class Headers
    {
        public const string Account = "x-efs-account";
        public const string UserProfileId = "x-efs-user-profile-id";
    }

    public record UserIdentity(string UserId, string UserType);

    /// <summary>
    /// Tenta resolver a identidade do usuário a partir dos headers da requisição.
    /// Retorna null e define errorMessage se os headers estiverem ausentes ou ambíguos.
    /// </summary>
    public UserIdentity? TryResolve(IHeaderDictionary headers, out string? errorMessage)
    {
        var account = headers[Headers.Account].FirstOrDefault();
        var profileId = headers[Headers.UserProfileId].FirstOrDefault();
        bool hasAccount = !string.IsNullOrWhiteSpace(account);
        bool hasProfileId = !string.IsNullOrWhiteSpace(profileId);

        if (!hasAccount && !hasProfileId)
        {
            errorMessage = $"Header de identificação ausente. Envie '{Headers.Account}' ou '{Headers.UserProfileId}'.";
            return null;
        }

        if (hasAccount && hasProfileId)
        {
            errorMessage = $"Envie apenas um header de identificação: '{Headers.Account}' ou '{Headers.UserProfileId}', não ambos.";
            return null;
        }

        errorMessage = null;
        return hasAccount
            ? new UserIdentity(account!, "cliente")
            : new UserIdentity(profileId!, "admin");
    }
}
