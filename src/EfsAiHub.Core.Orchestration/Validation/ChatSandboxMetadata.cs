namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Chaves e valores canônicos do <c>WorkflowDefinition.Metadata</c> usados
/// pelo subsistema de Chat Sandbox. Centralizar evita drift entre o caminho
/// que escreve (ChatSandboxService) e os que leem (calculadores, cleanup).
/// </summary>
public static class ChatSandboxMetadata
{
    /// <summary>Chave: <c>deploymentKind</c>. Identifica deploy tipo Chat.</summary>
    public const string DeploymentKindKey = "deploymentKind";

    /// <summary>Valor pro <c>deploymentKind</c> de Chat deploys (incluindo sandbox).</summary>
    public const string DeploymentKindChat = "chat";

    /// <summary>Chave: <c>kind</c>. Distingue sandbox de deploy real.</summary>
    public const string KindKey = "kind";

    /// <summary>Valor pro <c>kind</c> de workflows efêmeros de Chat Sandbox.</summary>
    public const string KindChatSandbox = "chat-sandbox";

    /// <summary>Chave: <c>chatSandboxSessionId</c>. Ref pra session que criou o workflow.</summary>
    public const string SessionIdKey = "chatSandboxSessionId";

    /// <summary>Chave: <c>transient</c>. Flag textual "true" pra cleanup detectar.</summary>
    public const string TransientKey = "transient";
}
