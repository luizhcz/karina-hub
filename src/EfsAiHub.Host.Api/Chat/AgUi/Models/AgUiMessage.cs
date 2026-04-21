namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

public sealed record AgUiMessage(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
