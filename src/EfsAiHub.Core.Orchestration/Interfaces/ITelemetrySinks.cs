using System.Threading.Channels;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.Capture;

namespace EfsAiHub.Core.Orchestration.Interfaces;

/// <summary>
/// Fase 5a — sink de uso de tokens exposto pelo host para o runtime de agentes.
/// Implementado pelo <c>TokenUsagePersistenceService</c> em Host.Api/Worker.
/// Permite que <c>AgentFactory</c> não dependa do tipo concreto do serviço de background.
/// </summary>
public interface ITokenUsageSink
{
    ChannelWriter<LlmTokenUsage> Writer { get; }
}

/// <summary>
/// Fase 5a — sink de invocações de tool exposto pelo host para o runtime de agentes.
/// Implementado pelo <c>ToolInvocationPersistenceService</c> em Host.Api/Worker.
/// </summary>
public interface IToolInvocationSink
{
    ChannelWriter<ToolInvocation> Writer { get; }
}

/// <summary>
/// Sink de captura de prompts LLM completos. Quando captura está OFF
/// (default), o middleware <c>LlmInvocationCaptureChatClient</c> nem chega
/// a escrever no channel — o sink existe pra que o middleware tenha um
/// writer estável injetável.
/// </summary>
public interface ILlmInvocationLogSink
{
    ChannelWriter<LlmInvocationLogEntry> Writer { get; }
}
