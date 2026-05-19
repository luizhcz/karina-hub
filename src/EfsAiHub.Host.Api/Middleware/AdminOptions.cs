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
    /// Lista de permissions que concedem acesso administrativo. Caller é
    /// reconhecido como admin quando alguma das permissions enviadas no header
    /// <c>x-efs-permissions</c> bate com algum item desta lista (match
    /// case-insensitive). Lista vazia → ninguém vira admin (UI admin trancada).
    /// </summary>
    public List<string> AdminPermissions { get; set; } = [];
}
