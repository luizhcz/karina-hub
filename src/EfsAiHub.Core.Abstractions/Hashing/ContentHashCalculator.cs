using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Abstractions.Hashing;

/// <summary>
/// Calcula SHA-256 canônico de um objeto serializado em JSON.
/// Reutilizado por AgentVersion e WorkflowVersion para idempotência por hash.
/// </summary>
public static class ContentHashCalculator
{
    public static string Compute<T>(T snapshot, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(snapshot, options ?? JsonDefaults.Domain);
        return ComputeFromString(json);
    }

    public static string ComputeFromString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeFromBytes(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
