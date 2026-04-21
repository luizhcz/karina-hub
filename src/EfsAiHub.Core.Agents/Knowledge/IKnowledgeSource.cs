namespace EfsAiHub.Core.Agents.Knowledge;

/// <summary>
/// Ponto de extensão para Fase 4 (RAG). Contratos estáveis para que providers
/// (pgvector, Azure AI Search, Foundry file_search) possam ser plugados sem
/// tocar o hot-path de construção de agentes.
///
/// Fase 3 deixa apenas as interfaces e tipos de dados; NÃO há implementação ainda.
/// Skills já referenciam <c>KnowledgeSourceIds</c> e o <c>AgentFactory</c>
/// consumirá <see cref="IKnowledgeRetriever"/> opcionalmente via DI quando a Fase 4 chegar.
/// </summary>
public interface IKnowledgeSource
{
    /// <summary>Identificador lógico — referenciado por <c>Skill.KnowledgeSourceIds</c>.</summary>
    string Id { get; }

    /// <summary>Tipo do backend: "Pgvector" | "AzureAiSearch" | "FoundryFileSearch".</summary>
    string Kind { get; }

    Task<IReadOnlyList<RetrievedDocument>> RetrieveAsync(
        RetrievalQuery query, CancellationToken ct = default);
}

/// <summary>
/// Entrada padronizada de consulta semântica. Extensível via <see cref="Filters"/>
/// (metadados, tenantId, etc.) sem quebrar assinatura.
/// </summary>
public sealed record RetrievalQuery(
    string Text,
    int TopK = 5,
    double? SimilarityThreshold = null,
    IReadOnlyDictionary<string, string>? Filters = null);

/// <summary>Resultado de uma consulta semântica.</summary>
public sealed record RetrievedDocument(
    string SourceId,
    string Content,
    double Score,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Descritor declarativo de um knowledge source — persistido em JSONB quando a Fase 4
/// introduzir a tabela <c>knowledge_sources</c>. Mantido aqui para congelar o contrato.
/// </summary>
public sealed record KnowledgeSourceDescriptor(
    string Id,
    string Kind,
    string? ConnectionRef,
    string? IndexName,
    string? EmbeddingModel,
    int TopK = 5,
    double? SimilarityThreshold = null);

/// <summary>
/// Orquestrador opcional: dado um conjunto de ids de knowledge sources (tipicamente
/// agregado das skills ativas do agente), executa a recuperação em paralelo e devolve
/// os documentos para injeção no contexto da execução.
///
/// Quando a Fase 4 for implementada, o <c>AgentFactory</c> passará a consultá-lo via DI
/// (opcional — null no MVP de Fase 3) antes da primeira chamada LLM.
/// </summary>
public interface IKnowledgeRetriever
{
    Task<IReadOnlyList<RetrievedDocument>> RetrieveForAgentAsync(
        IReadOnlyList<string> knowledgeSourceIds,
        RetrievalQuery query,
        CancellationToken ct = default);
}
