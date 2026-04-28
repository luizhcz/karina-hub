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

    /// <summary>
    /// Query param names usados como fallback quando o cliente não consegue
    /// enviar headers customizados (ex.: <c>EventSource</c>/SSE no browser
    /// não suporta headers). Endpoints públicos NÃO devem aceitar esses params
    /// — uso restrito a SSE.
    /// </summary>
    public static class QueryParams
    {
        public const string Account = "account";
        public const string UserProfileId = "userProfileId";
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
        return Resolve(account, profileId, out errorMessage,
            $"Header de identificação ausente. Envie '{Headers.Account}' ou '{Headers.UserProfileId}'.",
            $"Envie apenas um header de identificação: '{Headers.Account}' ou '{Headers.UserProfileId}', não ambos.");
    }

    /// <summary>
    /// Igual ao <see cref="TryResolve(IHeaderDictionary, out string?)"/>, mas com
    /// fallback para query params (<c>?account=</c> / <c>?userProfileId=</c>).
    /// Necessário pra <c>EventSource</c>/SSE no browser, que não envia headers
    /// customizados. Headers têm precedência — query é só fallback.
    /// </summary>
    public UserIdentity? TryResolve(HttpRequest request, out string? errorMessage)
    {
        // Headers primeiro (mantém comportamento existente em rotas normais).
        var fromHeaders = TryResolve(request.Headers, out _);
        if (fromHeaders != null)
        {
            errorMessage = null;
            return fromHeaders;
        }

        // Fallback para query params (rota SSE).
        var account = request.Query[QueryParams.Account].FirstOrDefault();
        var profileId = request.Query[QueryParams.UserProfileId].FirstOrDefault();
        return Resolve(account, profileId, out errorMessage,
            $"Identificação ausente. Envie header '{Headers.Account}'/'{Headers.UserProfileId}' ou query '{QueryParams.Account}'/'{QueryParams.UserProfileId}'.",
            $"Identificação ambígua: envie só um (header ou query, '{QueryParams.Account}' OU '{QueryParams.UserProfileId}').");
    }

    private static UserIdentity? Resolve(string? account, string? profileId, out string? errorMessage,
        string missingMessage, string ambiguousMessage)
    {
        bool hasAccount = !string.IsNullOrWhiteSpace(account);
        bool hasProfileId = !string.IsNullOrWhiteSpace(profileId);

        if (!hasAccount && !hasProfileId)
        {
            errorMessage = missingMessage;
            return null;
        }

        if (hasAccount && hasProfileId)
        {
            errorMessage = ambiguousMessage;
            return null;
        }

        errorMessage = null;
        return hasAccount
            ? new UserIdentity(account!, "cliente")
            : new UserIdentity(profileId!, "admin");
    }
}
