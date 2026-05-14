namespace EfsAiHub.Host.Api.Configuration;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Liga o gate de admin. Default true. Em false, qualquer request é
    /// tratado como admin — só pra ambientes dev/test em que setar headers
    /// de identidade atrapalha o ciclo de desenvolvimento. Deve estar true
    /// em homologação e produção.
    /// </summary>
    public bool GateEnabled { get; set; } = true;

    /// <summary>
    /// ExternalUserIds que viram admin no startup via UserBootstrapHostedService.
    /// Idempotente — força IsAdmin=TRUE em aihub.users mesmo se a row já existe.
    /// Não é consultado em runtime: o gating é decidido por users.IsAdmin no DB.
    /// Vazia = não bootstrap nenhum admin (DB precisa ter pelo menos um admin
    /// existente ou a UI admin fica inacessível até alguém ser promovido por SQL).
    /// </summary>
    public List<string> BootstrapAdminExternalUserIds { get; set; } = [];

    /// <summary>
    /// TenantId associado aos admins de bootstrap. Default "default" — ambientes
    /// multi-tenant devem setar explicitamente para o tenant alvo.
    /// </summary>
    public string BootstrapTenantId { get; set; } = "default";

    /// <summary>
    /// UserType atribuído aos admins de bootstrap (campo informativo).
    /// "admin" reflete a convenção do header x-efs-user-profile-id.
    /// </summary>
    public string BootstrapUserType { get; set; } = "admin";
}
