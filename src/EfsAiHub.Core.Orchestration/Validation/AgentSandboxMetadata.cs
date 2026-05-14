namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Chaves e valores canônicos do <c>WorkflowDefinition.Metadata</c> usados
/// pelo subsistema Agent Sandbox. Centralizar evita drift entre o caminho
/// que escreve (AgentSandboxService) e os que leem (calculadores, cleanup).
/// </summary>
public static class AgentSandboxMetadata
{
    /// <summary>Chave: <c>deploymentKind</c>. Identifica deploy tipo Chat ou Standalone.</summary>
    public const string DeploymentKindKey = "deploymentKind";

    /// <summary>Valor pro <c>deploymentKind</c> de Chat deploys (incluindo sandbox).</summary>
    public const string DeploymentKindChat = "chat";

    /// <summary>Valor pro <c>deploymentKind</c> de Standalone deploys (incluindo sandbox).</summary>
    public const string DeploymentKindStandalone = "standalone";

    /// <summary>Chave: <c>kind</c>. Distingue sandbox de deploy real.</summary>
    public const string KindKey = "kind";

    /// <summary>Valor pro <c>kind</c> de workflows efêmeros de Chat Sandbox.</summary>
    public const string KindChatSandbox = "chat-sandbox";

    /// <summary>Valor pro <c>kind</c> de workflows efêmeros de Standalone Sandbox.</summary>
    public const string KindStandaloneSandbox = "standalone-sandbox";

    /// <summary>
    /// Chave: <c>chatSandboxSessionId</c>. Ref pra session que criou o workflow.
    /// Mantida com nome legado pra preservar leitores existentes
    /// (frontend ChatDeploymentSandbox + queries de audit). O conceito hoje é
    /// "Agent Sandbox" — o rename da chave entra junto com o frontend update.
    /// </summary>
    public const string SessionIdKey = "chatSandboxSessionId";

    /// <summary>Chave: <c>transient</c>. Flag textual "true" pra cleanup detectar.</summary>
    public const string TransientKey = "transient";
}
