using System.Collections.Concurrent;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Registra e recupera DelegateExecutors para uso no modo Graph.
/// Thread-safe — seguro para uso como singleton com registros paralelos no startup.
/// </summary>
public class CodeExecutorRegistry : ICodeExecutorRegistry
{
    private readonly ConcurrentDictionary<string, Func<string, CancellationToken, Task<string>>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (Type Input, Type Output)> _typeInfo
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string functionName, Func<string, CancellationToken, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[functionName] = handler;
    }

    public DelegateExecutor CreateExecutor(string stepId, string functionName)
    {
        if (!_handlers.TryGetValue(functionName, out var handler))
            throw new KeyNotFoundException(
                $"Nenhum executor registrado com functionName='{functionName}'. " +
                $"Registre via ICodeExecutorRegistry.Register() no Program.cs.");

        return new DelegateExecutor(stepId, handler);
    }

    public bool Contains(string functionName) => _handlers.ContainsKey(functionName);

    public IReadOnlyCollection<string> GetRegisteredNames()
        => _handlers.Keys.ToArray();

    public void RegisterSchema(string functionName, Type inputType, Type outputType)
        => _typeInfo[functionName] = (inputType, outputType);

    public IReadOnlyDictionary<string, (string InputType, string OutputType)> GetTypeInfo()
        => _typeInfo.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.Input.Name, kv.Value.Output.Name));
}
