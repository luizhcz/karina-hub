namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Resolve identidade do usuário a partir dos headers HTTP.
/// Headers de identidade (exatamente um):
///   - x-efs-account            → userId com userType "cliente"
///   - x-efs-user-profile-id    → userId com userType "admin"
/// Header de permissões (obrigatório sempre que identidade é fornecida):
///   - x-efs-permissions        → CSV de permissões resolvidas pelo proxy/IdP
///                                (string vazia significa "autenticado, sem permissão").
///
/// "admin" no UserType apenas indica a origem do header (assessor/gestor/
/// consultor); o gate real é feito por <c>AdminPermissionEvaluator</c> a
/// partir da lista de permissões.
/// </summary>
public class UserIdentityResolver
{
    public static class Headers
    {
        public const string Account = "x-efs-account";
        public const string UserProfileId = "x-efs-user-profile-id";
        public const string Permissions = "x-efs-permissions";
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
        public const string Permissions = "permissions";
    }

    public record UserIdentity(string UserId, string UserType, IReadOnlyList<string> Permissions);

    /// <summary>
    /// Tenta resolver a identidade do usuário a partir dos headers da requisição.
    /// Retorna null e define errorMessage quando:
    ///   - headers de identidade ausentes/ambíguos, OU
    ///   - identidade resolvida mas <c>x-efs-permissions</c> não veio (header obrigatório).
    /// Quando nenhuma identidade foi enviada, retorna (null, null) — request anônimo,
    /// sem erro.
    /// </summary>
    public UserIdentity? TryResolve(IHeaderDictionary headers, out string? errorMessage)
    {
        var account = headers[Headers.Account].FirstOrDefault();
        var profileId = headers[Headers.UserProfileId].FirstOrDefault();
        var hasPermissionsHeader = headers.ContainsKey(Headers.Permissions);
        var permissionsRaw = hasPermissionsHeader ? headers[Headers.Permissions].FirstOrDefault() : null;
        return Resolve(account, profileId, hasPermissionsHeader, permissionsRaw, out errorMessage,
            $"Header de identificação ausente. Envie '{Headers.Account}' ou '{Headers.UserProfileId}'.",
            $"Envie apenas um header de identificação: '{Headers.Account}' ou '{Headers.UserProfileId}', não ambos.",
            $"Header '{Headers.Permissions}' é obrigatório quando identidade é fornecida.");
    }

    /// <summary>
    /// Igual ao <see cref="TryResolve(IHeaderDictionary, out string?)"/>, mas com
    /// fallback para query params (<c>?account=</c> / <c>?userProfileId=</c> /
    /// <c>?permissions=</c>). Necessário pra <c>EventSource</c>/SSE no browser,
    /// que não envia headers customizados. Headers têm precedência — query é só
    /// fallback.
    /// </summary>
    public UserIdentity? TryResolve(HttpRequest request, out string? errorMessage)
    {
        var fromHeaders = TryResolve(request.Headers, out var headerError);
        if (fromHeaders != null)
        {
            errorMessage = null;
            return fromHeaders;
        }

        // Identidade veio mas falhou validação (ex.: ambígua ou sem permissions):
        // propaga o erro sem cair pra query, pra que a mensagem fique consistente.
        var hasAnyIdentityHeader =
            !string.IsNullOrWhiteSpace(request.Headers[Headers.Account].FirstOrDefault())
            || !string.IsNullOrWhiteSpace(request.Headers[Headers.UserProfileId].FirstOrDefault());
        if (hasAnyIdentityHeader)
        {
            errorMessage = headerError;
            return null;
        }

        var account = request.Query[QueryParams.Account].FirstOrDefault();
        var profileId = request.Query[QueryParams.UserProfileId].FirstOrDefault();
        var hasPermissionsQuery = request.Query.ContainsKey(QueryParams.Permissions);
        var permissionsRaw = hasPermissionsQuery ? request.Query[QueryParams.Permissions].FirstOrDefault() : null;
        return Resolve(account, profileId, hasPermissionsQuery, permissionsRaw, out errorMessage,
            $"Identificação ausente. Envie header '{Headers.Account}'/'{Headers.UserProfileId}' ou query '{QueryParams.Account}'/'{QueryParams.UserProfileId}'.",
            $"Identificação ambígua: envie só um (header ou query, '{QueryParams.Account}' OU '{QueryParams.UserProfileId}').",
            $"Permissões ausentes. Envie header '{Headers.Permissions}' ou query '{QueryParams.Permissions}' quando a identidade é fornecida.");
    }

    private static UserIdentity? Resolve(
        string? account,
        string? profileId,
        bool hasPermissionsInput,
        string? permissionsRaw,
        out string? errorMessage,
        string missingIdentityMessage,
        string ambiguousIdentityMessage,
        string missingPermissionsMessage)
    {
        bool hasAccount = !string.IsNullOrWhiteSpace(account);
        bool hasProfileId = !string.IsNullOrWhiteSpace(profileId);

        if (!hasAccount && !hasProfileId)
        {
            errorMessage = missingIdentityMessage;
            return null;
        }

        if (hasAccount && hasProfileId)
        {
            errorMessage = ambiguousIdentityMessage;
            return null;
        }

        // Header presente porém ausente diferencia "esqueceu de enviar" (erro)
        // de "enviou vazio" (autenticado, sem permissão). Vazio é válido.
        if (!hasPermissionsInput)
        {
            errorMessage = missingPermissionsMessage;
            return null;
        }

        errorMessage = null;
        var permissions = ParsePermissions(permissionsRaw);
        return hasAccount
            ? new UserIdentity(account!, "cliente", permissions)
            : new UserIdentity(profileId!, "admin", permissions);
    }

    /// <summary>
    /// Parseia a lista CSV de permissions: split por vírgula, trim, lowercase,
    /// dedupe, descarta entradas vazias. Normaliza pra que match contra
    /// <c>Admin:AdminPermissions</c> seja case-insensitive sem custo por request.
    /// </summary>
    public static IReadOnlyList<string> ParsePermissions(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var token in raw.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0) continue;
            var normalized = trimmed.ToLowerInvariant();
            if (seen.Add(normalized))
                result.Add(normalized);
        }
        return result;
    }
}
