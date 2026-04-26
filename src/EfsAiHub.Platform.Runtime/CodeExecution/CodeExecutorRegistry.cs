using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    private readonly ILogger<CodeExecutorRegistry> _logger;

    /// <summary>
    /// Opções dedicadas ao schema export — congeladas após o primeiro uso. NÃO reutilizar
    /// pra Serialize/Deserialize: o TypeInfoResolver setado aqui é só pro JsonSchemaExporter
    /// e a instance fica read-only depois da primeira chamada.
    /// </summary>
    private static readonly JsonSerializerOptions SchemaExportOptions =
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

    public CodeExecutorRegistry(ILogger<CodeExecutorRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<CodeExecutorRegistry>.Instance;
    }

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

    public IReadOnlyDictionary<string, ExecutorSchemaInfo> GetSchemas()
    {
        var result = new Dictionary<string, ExecutorSchemaInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, types) in _typeInfo)
        {
            try
            {
                var inputNode = SchemaExportOptions.GetJsonSchemaAsNode(types.Input);
                var outputNode = SchemaExportOptions.GetJsonSchemaAsNode(types.Output);

                // Clone() desacopla o JsonElement do JsonDocument pooled-buffer subjacente:
                // sem Clone(), manter o JsonElement em ExecutorSchemaInfo segura o buffer
                // do ArrayPool e ele nunca volta pro pool (drain silencioso). Com Clone()
                // o using do JsonDocument pode disposar o buffer logo.
                using var inputDoc = JsonDocument.Parse(inputNode.ToJsonString());
                var outputJson = outputNode.ToJsonString();
                using var outputDoc = JsonDocument.Parse(outputJson);

                result[name] = new ExecutorSchemaInfo(
                    inputDoc.RootElement.Clone(),
                    outputDoc.RootElement.Clone(),
                    ComputeShortHash(outputJson));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Tipo não-suportado pelo exporter (generics abertos, polymorphism sem
                // [JsonDerivedType], JsonNumberHandling exotic, etc) — pula esse executor:
                // aparece sem schema na UI mas não impede deploy. Logamos com stack pra
                // facilitar debug; OOM e similares passam adiante.
                _logger.LogWarning(ex,
                    "[CodeExecutorRegistry] JsonSchemaExporter falhou para '{Executor}' (Input={InputType}, Output={OutputType}). Executor segue registrado mas sem schema na UI.",
                    name, types.Input.FullName, types.Output.FullName);
            }
        }

        return result;
    }

    private static string ComputeShortHash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
