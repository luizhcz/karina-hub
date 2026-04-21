using System.Text.Json;

namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

public sealed record AgUiFrontendTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>JSON Schema dos parâmetros da tool.</summary>
    public JsonElement? Parameters { get; init; }
}
