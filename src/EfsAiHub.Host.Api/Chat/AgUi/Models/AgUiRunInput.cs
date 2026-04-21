using System.Text.Json;
using EfsAiHub.Host.Api.Chat.AgUi.State;

namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

/// <summary>
/// Input enviado pelo frontend para iniciar um run AG-UI.
/// </summary>
public sealed record AgUiRunInput
{
    /// <summary>ID da conversa (thread). Null = criar nova.</summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// ID do workflow a executar. Null = usa header <c>x-efs-workflow-id</c> ou workflow padrão do projeto.
    /// Prefira enviar via header <c>x-efs-workflow-id</c> para manter o body alinhado ao schema AG-UI padrão.
    /// </summary>
    public string? WorkflowId { get; init; }

    /// <summary>
    /// Histórico de mensagens anteriores, incluindo respostas de aprovação (role=tool).
    /// Usado para HITL via request_approval: o frontend envia a resposta de aprovação
    /// como mensagem com role=tool e ToolCallId=interactionId.
    /// </summary>
    public IReadOnlyList<AgUiInputMessage>? Messages { get; init; }

    /// <summary>
    /// Configuração de estado preditivo: mapeia argumentos de tool calls para campos de estado.
    /// Permite emitir STATE_DELTA durante o streaming de argumentos antes da tool call completar.
    /// </summary>
    public PredictiveStateConfig? PredictiveState { get; init; }

    /// <summary>Run ID fornecido pelo cliente. Propagado para os eventos RUN_STARTED/RUN_FINISHED.</summary>
    public string? RunId { get; init; }

    /// <summary>Tools disponíveis no frontend (o agente pode pedir para executar).</summary>
    public AgUiFrontendTool[]? Tools { get; init; }

    /// <summary>Estado atual do frontend (ex: tela aberta, seleção ativa).</summary>
    public JsonElement? State { get; init; }

    /// <summary>Contexto adicional passado pelo cliente AG-UI (aceito e repassado).</summary>
    public JsonElement? Context { get; init; }
}
