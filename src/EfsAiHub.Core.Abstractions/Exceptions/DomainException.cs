namespace EfsAiHub.Core.Abstractions.Exceptions;

/// <summary>
/// Exceção lançada quando uma invariante de domínio é violada.
/// Distinta de <see cref="ArgumentException"/> (que sinaliza argumento técnico inválido):
/// um <c>DomainException</c> significa "a regra de negócio diz que isso não pode existir".
/// </summary>
/// <remarks>
/// Disparada por factory methods <c>Create()</c> em entidades como <c>WorkflowDefinition</c>,
/// <c>Project</c> e <c>AgentDefinition</c> quando o estado construído seria inválido
/// (ex: modo Sequential sem agentes, Graph sem edges, edge referenciando node inexistente).
/// Deve ser mapeada pelo handler global de exceções para HTTP 400 com mensagem clara ao cliente.
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
