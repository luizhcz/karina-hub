namespace EfsAiHub.Core.Abstractions.Secrets;

/// <summary>
/// Loja in-memory de segredos resolvidos no boot. Lookups são síncronos,
/// thread-safe (snapshot imutável) e nunca tocam o secret manager externo.
///
/// Segredos novos ou rotacionados em runtime exigem reboot da app — política
/// operacional documentada (rotação = restart do pod). Em troca, runtime
/// fica imune a outage do secret manager.
/// </summary>
public interface IRuntimeSecretStore
{
    /// <summary>
    /// Resolve uma referência. Aceita:
    /// <list type="bullet">
    ///   <item>null/empty → retorna null.</item>
    ///   <item>Literal (sem prefixo <c>secret://</c>) → passa direto, com warning.</item>
    ///   <item>AWS ref (<c>secret://aws/{id}</c>) → lookup no snapshot in-memory.</item>
    /// </list>
    /// AWS ref que não foi preloaded retorna null + log de erro — caller decide
    /// se o nulo é fatal (a maioria já tem essa validação).
    /// </summary>
    string? Get(string? referenceOrLiteral);

    /// <summary>
    /// Total de identificadores AWS resolvidos no boot. Usado pelo health check
    /// e pelo log de startup pra ops conferir o preload.
    /// </summary>
    int LoadedCount { get; }
}
