using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Json.Schema;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Cache de <see cref="JsonSchema"/> parseados keyed por hash do schema raw.
/// Parsear/compilar um schema custa milissegundos; reusar a instância
/// derruba pra microssegundos. Key é hash do JSON cru, então admin editar
/// o schema invalida automaticamente (nova chave = novo entry).
///
/// Tamanho limitado pra evitar memory leak em deploys long-lived que
/// passam por muitos schemas distintos ao longo do tempo. Eviction
/// simples por capacidade — quando atinge limite, descarta o entry mais
/// antigo (FIFO via order de insert tracked num <c>ConcurrentQueue</c>
/// auxiliar). LRU exata exigiria locking; FIFO é "good enough" pro
/// padrão real (poucos schemas ativos por instância).
/// </summary>
public sealed class SchemaCache
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, JsonSchema> _entries;
    private readonly ConcurrentQueue<string> _insertOrder;

    public SchemaCache(int capacity = 500)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "SchemaCache.capacity deve ser > 0.");
        _capacity = capacity;
        _entries = new ConcurrentDictionary<string, JsonSchema>(StringComparer.Ordinal);
        _insertOrder = new ConcurrentQueue<string>();
    }

    public int Count => _entries.Count;

    /// <summary>
    /// Retorna o schema parseado pro JSON raw fornecido. Parse acontece
    /// uma única vez por hash; reuses subsequentes batem cache. Lança
    /// <see cref="JsonSchemaException"/> em JSON malformado (caller decide
    /// se cai em bypass + log).
    /// </summary>
    public JsonSchema GetOrAdd(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            throw new ArgumentException("schemaJson não pode ser vazio.", nameof(schemaJson));

        var key = HashSchema(schemaJson);
        if (_entries.TryGetValue(key, out var cached))
            return cached;

        var parsed = JsonSchema.FromText(schemaJson)
            ?? throw new JsonSchemaException("Schema JSON parseou mas resultou em null.");

        if (_entries.TryAdd(key, parsed))
        {
            _insertOrder.Enqueue(key);
            EvictIfNeeded();
            return parsed;
        }
        // Outro thread venceu a corrida ou eviction descartou — devolver o
        // que tiver no cache, ou fallback pro parse local se o eviction
        // removeu antes de ler. Sem race window de NPE.
        return _entries.TryGetValue(key, out var existing) ? existing : parsed;
    }

    /// <summary>Diagnóstico — usado por testes pra verificar reuso de instância.</summary>
    public bool TryGet(string schemaJson, out JsonSchema? schema)
    {
        var key = HashSchema(schemaJson);
        if (_entries.TryGetValue(key, out var hit))
        {
            schema = hit;
            return true;
        }
        schema = null;
        return false;
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > _capacity && _insertOrder.TryDequeue(out var oldKey))
        {
            _entries.TryRemove(oldKey, out _);
        }
    }

    private static string HashSchema(string schemaJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>Levantada quando schema JSON é malformado e não pode ser parseado.</summary>
public sealed class JsonSchemaException : Exception
{
    public JsonSchemaException(string message) : base(message) { }
    public JsonSchemaException(string message, Exception inner) : base(message, inner) { }
}
