using System.Text.Json;

namespace EfsAiHub.Core.Orchestration.Interfaces;

/// <summary>
/// Extension methods para ICodeExecutorRegistry com registro tipado via ICodeExecutor&lt;TInput, TOutput&gt;.
/// </summary>
public static class CodeExecutorRegistryExtensions
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registra um ICodeExecutor tipado. A serialização JSON é tratada automaticamente.
    /// </summary>
    public static void Register<TInput, TOutput>(
        this ICodeExecutorRegistry registry,
        string functionName,
        ICodeExecutor<TInput, TOutput> executor)
    {
        registry.Register(functionName, async (input, ct) =>
        {
            var typedInput = JsonSerializer.Deserialize<TInput>(input, _jsonOpts)
                ?? throw new InvalidOperationException(
                    $"Executor '{functionName}': falha ao deserializar input para {typeof(TInput).Name}. Input recebido: {input[..Math.Min(200, input.Length)]}");

            var result = await executor.ExecuteAsync(typedInput, ct);
            return JsonSerializer.Serialize(result, _jsonOpts);
        });
        registry.RegisterSchema(functionName, typeof(TInput), typeof(TOutput));
    }

    /// <summary>
    /// Registra uma função tipada inline sem precisar de uma classe separada.
    /// </summary>
    public static void Register<TInput, TOutput>(
        this ICodeExecutorRegistry registry,
        string functionName,
        Func<TInput, CancellationToken, Task<TOutput>> handler)
    {
        registry.Register(functionName, async (input, ct) =>
        {
            var typedInput = JsonSerializer.Deserialize<TInput>(input, _jsonOpts)
                ?? throw new InvalidOperationException(
                    $"Executor '{functionName}': falha ao deserializar input para {typeof(TInput).Name}. Input recebido: {input[..Math.Min(200, input.Length)]}");

            var result = await handler(typedInput, ct);
            return JsonSerializer.Serialize(result, _jsonOpts);
        });
        registry.RegisterSchema(functionName, typeof(TInput), typeof(TOutput));
    }
}
