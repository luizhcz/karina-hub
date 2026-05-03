using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Persistência de Generic Tools — strictamente owner-only via query filter por
/// ProjectId. Tool de project A é invisível pra project B mesmo via Id direto.
/// </summary>
public interface IGenericToolRepository
{
    /// <summary>Busca tool pelo Id no scope do projeto atual; null se não existir.</summary>
    Task<GenericTool?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Busca tool pelo Id no scope explícito de um projeto, ignorando o QueryFilter
    /// global do contexto atual. Usado durante o bind de recursos do agent (tools,
    /// MCPs, presets) onde o caller pode estar num project diferente do agent
    /// (ex.: agent com Visibility=global invocado de outro project, ou sandbox que
    /// não recebe header de project do frontend). Retorna null se não existir
    /// nesse projeto.
    /// </summary>
    Task<GenericTool?> GetByIdAsync(string id, string projectId, CancellationToken ct = default);

    /// <summary>Lista tools do projeto atual ordenadas por UpdatedAt desc.</summary>
    Task<IReadOnlyList<GenericTool>> ListAsync(CancellationToken ct = default);

    /// <summary>True se já existe tool com mesmo Name no projeto (case-sensitive). Usado
    /// pra validar unique constraint antes do INSERT.</summary>
    Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default);

    /// <summary>Insere uma nova tool. Lança quando o Id já existir.</summary>
    Task<GenericTool> CreateAsync(GenericTool tool, CancellationToken ct = default);

    /// <summary>
    /// Atualiza com optimistic concurrency: WHERE Id=@id AND UpdatedAt=@expected.
    /// 0 rows afetadas lança <see cref="GenericToolConcurrencyException"/>.
    /// </summary>
    Task<GenericTool> UpdateAsync(
        GenericTool tool,
        DateTime expectedUpdatedAt,
        CancellationToken ct = default);

    /// <summary>True se tool existia e foi removida; false se não existia.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class GenericToolConcurrencyException : Exception
{
    public GenericToolConcurrencyException(string toolId)
        : base($"GenericTool '{toolId}' foi modificado por outra requisição (UpdatedAt divergente).")
    { }
}

public sealed class GenericToolNameConflictException : Exception
{
    public GenericToolNameConflictException(string name)
        : base($"Já existe um GenericTool com Name='{name}' nesse projeto.")
    { }
}
