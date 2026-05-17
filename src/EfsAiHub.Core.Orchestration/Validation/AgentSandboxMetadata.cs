using EfsAiHub.Core.Orchestration.Workflows;

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

    /// <summary>
    /// Indica se o workflow é um sandbox efêmero de chat (marcado pelo
    /// <see cref="AgentSandboxService"/> com <c>kind=chat-sandbox</c>).
    /// Defensivo contra <c>Metadata</c> null. Single source of truth pro
    /// critério "é sandbox?" — consumido pelo gate de chat-deployment
    /// (<c>WorkflowService.EnsureChatDeploymentAllowedAsync</c>) e pelo
    /// <c>ChatValidationWarningCalculator</c>.
    ///
    /// FOLLOW-UP DE SEGURANÇA (escopo separado): caller externo pode forjar
    /// <c>metadata['kind']='chat-sandbox'</c> via POST /workflows direto
    /// pra escapar do gate de chat_deployment_allowed. Mitigação completa
    /// exige coerência check (<c>transient=true</c> + <c>chatSandboxSessionId</c>
    /// não-vazio + <c>Id</c> prefixado <c>deploy-chat-sandbox-</c>), ou parâmetro
    /// explícito no contrato de <c>IWorkflowService.CreateAsync</c>.
    /// Tracked como gap consciente nesta entrega — vire ticket de hardening.
    /// </summary>
    public static bool IsChatSandbox(WorkflowDefinition def) =>
        def.Metadata is not null
        && def.Metadata.TryGetValue(KindKey, out var kind)
        && string.Equals(kind, KindChatSandbox, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Indica se o workflow é um deploy de chat de produção — isto é,
    /// <c>deploymentKind=chat</c> mas <b>não</b> sandbox. Sandbox tem
    /// <c>deploymentKind=chat</c> também, mas o flag <c>kind=chat-sandbox</c>
    /// reduz pra workflow efêmero pra teste; gate de
    /// <c>chat_deployment_allowed</c> e warnings de validação ignoram esse caso.
    /// </summary>
    public static bool IsChatProductionDeploy(WorkflowDefinition def) =>
        def.Metadata is not null
        && def.Metadata.TryGetValue(DeploymentKindKey, out var dk)
        && string.Equals(dk, DeploymentKindChat, System.StringComparison.OrdinalIgnoreCase)
        && !IsChatSandbox(def);
}
