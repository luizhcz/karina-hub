namespace EfsAiHub.Host.Api.Configuration;

public class ChatRoutingOptions
{
    public const string SectionName = "ChatRouting";

    /// <summary>
    /// Mapeamento de userType → workflowId padrão.
    /// Ex: { "cliente": "atendimento-cliente", "assessor": "atendimento-assessor" }
    /// </summary>
    public Dictionary<string, string> DefaultWorkflows { get; set; } = [];
}
