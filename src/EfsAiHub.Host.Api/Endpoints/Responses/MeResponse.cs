namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Resposta do <c>GET /api/aihub/me</c>. Identidade resolvida do caller +
/// flag de admin + metadados do usuário persistido. Sempre 200; quando
/// headers de identificação estão ausentes, retorna campos null +
/// <c>IsAdmin=false</c>.
/// </summary>
public sealed class MeResponse
{
    /// <summary>AccountId do header <c>x-efs-account</c> ou ProfileId do
    /// <c>x-efs-user-profile-id</c>. Null se nenhum dos dois foi enviado.</summary>
    public string? AccountId { get; init; }

    /// <summary>"cliente" quando vem via <c>x-efs-account</c>; "admin" quando
    /// vem via <c>x-efs-user-profile-id</c>. Null sem identidade.</summary>
    public string? UserType { get; init; }

    /// <summary>True quando o usuário persistido tem <c>IsAdmin=true</c>.
    /// Falso sem identidade ou quando o provisioning falhou.</summary>
    public bool IsAdmin { get; init; }

    /// <summary>Id interno do usuário no diretório (aihub.users.Id). Null
    /// quando o request veio sem identidade.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Nome humano do usuário (default = ExternalUserId enquanto o
    /// admin não personalizar via UI).</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Projetos visíveis ao usuário. Admin recebe todos os projetos do tenant
    /// (exceto "default" que é admin-only e fica omitido aqui). Non-admin
    /// recebe apenas projetos com vínculo em user_projects. Lista vazia
    /// quando non-admin ainda não tem nenhum vínculo — frontend redireciona
    /// pra /bem-vindo nesse caso.
    /// </summary>
    public IReadOnlyList<ProjectRef> Projects { get; init; } = [];
}

/// <summary>Resumo de projeto incluído no /me response. Só id + nome humano.</summary>
public sealed class ProjectRef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
