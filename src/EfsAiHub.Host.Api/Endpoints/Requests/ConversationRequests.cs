namespace EfsAiHub.Host.Api.Models.Requests;

public record CreateConversationRequest(string? WorkflowId = null, Dictionary<string, string>? Metadata = null);

public record ChatMessageInputDto(string Role, string Message);
