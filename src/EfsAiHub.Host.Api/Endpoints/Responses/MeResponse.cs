namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Resposta do <c>GET /api/aihub/me</c>. Identidade resolvida do caller +
/// flag de admin. Sempre 200; quando headers de identificação estão ausentes,
/// retorna campos null + <c>IsAdmin=false</c>.
/// </summary>
public sealed class MeResponse
{
    /// <summary>AccountId do header <c>x-efs-account</c> ou ProfileId do
    /// <c>x-efs-user-profile-id</c>. Null se nenhum dos dois foi enviado.</summary>
    public string? AccountId { get; init; }

    /// <summary>"cliente" quando vem via <c>x-efs-account</c>; "admin" quando
    /// vem via <c>x-efs-user-profile-id</c>. Null sem identidade.</summary>
    public string? UserType { get; init; }

    /// <summary>True quando o <c>AccountId</c> consta em <c>AdminOptions.AccountIds</c>,
    /// ou quando o gate está desabilitado (lista vazia). False sem identidade.</summary>
    public bool IsAdmin { get; init; }
}
