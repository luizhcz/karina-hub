namespace EfsAiHub.Platform.Runtime.Interfaces;

/// <summary>
/// Executa um único workflow: constrói o grafo, monta o mapa de nomes de agentes e
/// aciona o runner. Não persiste estado — o caller é responsável por tratar exceções
/// e atualizar o status no banco.
/// </summary>
public interface IWorkflowExecutor
{
    Task ExecuteAsync(
        WorkflowExecution execution,
        WorkflowDefinition definition,
        CancellationToken ct = default);
}
