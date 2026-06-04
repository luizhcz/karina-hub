namespace EfsAiHub.Core.Abstractions.Secrets;

/// <summary>
/// Source de identificadores AWS pro preloader resolver no boot. Cada lugar
/// que pode guardar uma referência <c>secret://aws/...</c> (config, projects,
/// agents, etc.) registra um source. O preloader agrega + deduplica antes de
/// chamar o secret manager.
/// </summary>
public interface ISecretReferenceSource
{
    /// <summary>Nome do source pra log de boot (ex.: "projects", "agents").</summary>
    string Name { get; }

    /// <summary>
    /// Coleta as referências brutas. Cada string deve ser uma referência
    /// <c>secret://aws/...</c> (literal/empty são filtrados pelo preloader).
    /// Falhas devem ser logadas internamente; retornar conjunto vazio é
    /// preferível a lançar (não derrubar o boot inteiro por causa de um source).
    /// </summary>
    Task<IReadOnlyCollection<string>> CollectAsync(CancellationToken ct);
}
