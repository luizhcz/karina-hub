using System.Text.Json;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/aihub/generic-tools/{id}/execute. Args é o dict que o
/// builder de request usaria como payload do agente — chaves casam com path /
/// query / body params da tool. Vem como JsonElement pra preservar tipo
/// (number/string/bool/array/object) sem coerção precoce.
/// </summary>
public sealed class ExecuteGenericToolRequest
{
    public Dictionary<string, JsonElement> Args { get; init; } = new();
}
