namespace EfsAiHub.Core.Orchestration.Interfaces;

/// <summary>
/// Executor de código tipado para uso em workflows Graph.
/// Implemente esta interface para evitar serialização/deserialização JSON manual.
///
/// Uso:
///   public class MyExecutor : ICodeExecutor&lt;MyInput, MyOutput&gt;
///   {
///       public async Task&lt;MyOutput&gt; ExecuteAsync(MyInput input, CancellationToken ct)
///       {
///           // código tipado aqui — sem JsonSerializer manual
///           return new MyOutput { ... };
///       }
///   }
///
///   // Registro com uma linha:
///   registry.Register("my_executor", new MyExecutor());
/// </summary>
public interface ICodeExecutor<TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct);
}
