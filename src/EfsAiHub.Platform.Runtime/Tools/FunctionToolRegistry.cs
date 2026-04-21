using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Fase 6 — Registry versionado por fingerprint. Cada <see cref="Register"/> computa
/// um hash SHA-256 canônico de <c>{Name, Description, JsonSchema}</c> e indexa em
/// <c>(Name, Hash) → AIFunction</c>. Um ponteiro "latest" por nome preserva o
/// comportamento legado de <see cref="Get(string)"/>/<see cref="TryGet"/>.
/// </summary>
public class FunctionToolRegistry : IFunctionToolRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<string>> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIFunction> _byFingerprint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;

    public FunctionToolRegistry(ILogger? logger = null) => _logger = logger;

    // Project-scoped: (projectId, name) → AIFunction (latest per project)
    private readonly Dictionary<string, AIFunction> _projectTools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, AIFunction function)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name obrigatório.", nameof(name));
        ArgumentNullException.ThrowIfNull(function);

        if (string.IsNullOrWhiteSpace(function.Description))
            _logger?.LogWarning(
                "Tool '{ToolName}' registrada sem [Description]. " +
                "O LLM pode não entender quando e como chamá-la. " +
                "Adicione [Description(\"...\"] no método C# correspondente.",
                name);

        var hash = ComputeFingerprint(function);
        lock (_lock)
        {
            _byFingerprint[Key(name, hash)] = function;
            if (!_versions.TryGetValue(name, out var list))
            {
                list = new List<string>();
                _versions[name] = list;
            }
            if (!list.Contains(hash)) list.Add(hash);
            _latest[name] = hash;
        }
    }

    public AIFunction Get(string name)
    {
        if (TryGet(name, out var fn) && fn is not null) return fn;
        throw new KeyNotFoundException($"Function tool '{name}' não está registrada no FunctionToolRegistry.");
    }

    public bool TryGet(string name, out AIFunction? function)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(name, out var hash) &&
                _byFingerprint.TryGetValue(Key(name, hash), out var fn))
            {
                function = fn;
                return true;
            }
            function = null;
            return false;
        }
    }

    public AIFunction? GetByFingerprint(string name, string fingerprintHash)
    {
        if (string.IsNullOrEmpty(fingerprintHash)) return null;
        lock (_lock)
        {
            return _byFingerprint.TryGetValue(Key(name, fingerprintHash), out var fn) ? fn : null;
        }
    }

    public string? GetLatestFingerprint(string name)
    {
        lock (_lock) { return _latest.TryGetValue(name, out var hash) ? hash : null; }
    }

    public IReadOnlyList<string> ListFingerprints(string name)
    {
        lock (_lock)
        {
            return _versions.TryGetValue(name, out var list) ? list.ToArray() : Array.Empty<string>();
        }
    }

    public IReadOnlyDictionary<string, AIFunction> GetAll()
    {
        lock (_lock)
        {
            var snap = new Dictionary<string, AIFunction>(_latest.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, hash) in _latest)
                if (_byFingerprint.TryGetValue(Key(name, hash), out var fn))
                    snap[name] = fn;
            return snap;
        }
    }

    // ── Project-scoped tools ────────────────────────────────────────────────

    public void Register(string name, AIFunction function, string projectId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name obrigatório.", nameof(name));
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("projectId obrigatório.", nameof(projectId));
        ArgumentNullException.ThrowIfNull(function);

        lock (_lock)
        {
            _projectTools[ProjectKey(projectId, name)] = function;
        }
    }

    public bool TryGet(string name, string projectId, out AIFunction? function)
    {
        lock (_lock)
        {
            // Project-scoped first, fallback to global
            if (_projectTools.TryGetValue(ProjectKey(projectId, name), out var projFn))
            {
                function = projFn;
                return true;
            }
            return TryGet(name, out function);
        }
    }

    public IReadOnlyDictionary<string, AIFunction> GetAll(string projectId)
    {
        lock (_lock)
        {
            // Start with global tools
            var snap = new Dictionary<string, AIFunction>(_latest.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, hash) in _latest)
                if (_byFingerprint.TryGetValue(Key(name, hash), out var fn))
                    snap[name] = fn;

            // Override/add with project-scoped tools
            var prefix = projectId + "|";
            foreach (var (key, fn) in _projectTools)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var toolName = key[prefix.Length..];
                    snap[toolName] = fn;
                }
            }

            return snap;
        }
    }

    private static string ProjectKey(string projectId, string name) => $"{projectId}|{name}";

    /// <summary>
    /// Computa um hash canônico <c>sha256(name|description|json_schema)</c>.
    /// Estável entre processos — usado para snapshot em <c>AgentVersion</c>.
    /// </summary>
    public static string ComputeFingerprint(AIFunction function)
    {
        var sb = new StringBuilder();
        sb.Append(function.Name).Append('|');
        sb.Append(function.Description ?? string.Empty).Append('|');
        try { sb.Append(function.JsonSchema.GetRawText()); }
        catch { sb.Append("{}"); }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Key(string name, string hash) => $"{name}|{hash}";
}
