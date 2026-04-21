using EfsAiHub.Core.Orchestration.Executors;

namespace EfsAiHub.Core.Orchestration.Interfaces;

/// <summary>
/// Registra e recupera executores de código puro (sem LLM) para uso no modo Graph.
/// Cada executor é identificado por um nome e encapsula uma função C# assíncrona string→string.
///
/// Uso — 3 passos:
/// <list type="number">
///   <item>Implemente a função: <c>async Task&lt;string&gt; MyFunc(string input, CancellationToken ct)</c></item>
///   <item>Registre no Program.cs: <c>registry.Register("my_executor", MyFunc)</c></item>
///   <item>Referencie no workflow: <c>executors[].functionName = "my_executor"</c></item>
/// </list>
///
/// Exemplo rápido:
/// <code>
///   registry.Register("enrich_data", async (input, ct) =>
///   {
///       var data = JsonSerializer.Deserialize&lt;MyModel&gt;(input);
///       var result = await EnrichAsync(data, ct);
///       return JsonSerializer.Serialize(result);
///   });
/// </code>
///
/// O functionName usado em Register() deve ser idêntico ao WorkflowExecutorStep.FunctionName
/// na definição do workflow. Veja CONTRIBUTING.md para o guia completo.
///
/// Para input/output tipado sem JSON manual use <see cref="CodeExecutorRegistryExtensions.Register{TInput,TOutput}(ICodeExecutorRegistry,string,ICodeExecutor{TInput,TOutput})"/>.
/// </summary>
public interface ICodeExecutorRegistry
{
    /// <summary>
    /// Registra um handler como executor de código.
    /// </summary>
    /// <param name="functionName">Nome único (referenciado em WorkflowExecutorStep.FunctionName).</param>
    /// <param name="handler">Função que recebe o input string e retorna o output string.</param>
    void Register(string functionName, Func<string, CancellationToken, Task<string>> handler);

    /// <summary>
    /// Cria um DelegateExecutor com o ID e o handler registrado para functionName.
    /// </summary>
    /// <param name="stepId">ID do passo no workflow (usado como ID do executor).</param>
    /// <param name="functionName">Nome da função registrada.</param>
    DelegateExecutor CreateExecutor(string stepId, string functionName);

    bool Contains(string functionName);

    /// <summary>
    /// Retorna os nomes de todos os executores registrados.
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredNames();

    /// <summary>
    /// Registra os tipos de input/output para um executor tipado.
    /// Default no-op — implementações existentes não precisam sobrescrever.
    /// </summary>
    void RegisterSchema(string functionName, Type inputType, Type outputType) { }

    /// <summary>
    /// Retorna o mapeamento nome → (InputType, OutputType) para executores registrados via overload tipado.
    /// Executors registrados como lambda pura retornam dicionário vazio.
    /// </summary>
    IReadOnlyDictionary<string, (string InputType, string OutputType)> GetTypeInfo()
        => new Dictionary<string, (string InputType, string OutputType)>();
}
