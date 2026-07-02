using EfsAiHub.Host.Worker.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace EfsAiHub.Tests.Unit.Workers;

/// <summary>
/// O <see cref="execution.Output"/> de um workflow deve levar apenas o output do
/// agente final — exceto em Concurrent, cujo modo paralelo combina as respostas.
/// </summary>
public sealed class WorkflowRunnerOutputSelectionTests
{
    private static ChatMessage Assistant(string text) => new(ChatRole.Assistant, text);

    [Fact]
    public void Concurrent_com_dois_agentes_combina_as_respostas()
    {
        var msgs = new[] { Assistant("resposta A"), Assistant("resposta B") };

        var output = WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Concurrent, msgs);

        output.Should().Be("resposta A\n\n---\n\nresposta B");
    }

    [Theory]
    [InlineData(OrchestrationMode.Handoff)]
    [InlineData(OrchestrationMode.GroupChat)]
    [InlineData(OrchestrationMode.Sequential)]
    [InlineData(OrchestrationMode.Graph)]
    public void Modos_nao_concurrent_levam_apenas_o_ultimo_agente(OrchestrationMode mode)
    {
        var msgs = new[] { Assistant("primeiro agente"), Assistant("agente final") };

        var output = WorkflowRunnerService.SelectAgentOutput(mode, msgs);

        output.Should().Be("agente final");
    }

    [Fact]
    public void Mensagem_unica_e_retornada_em_qualquer_modo()
    {
        var msgs = new[] { Assistant("única resposta") };

        WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Concurrent, msgs)
            .Should().Be("única resposta");
        WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Handoff, msgs)
            .Should().Be("única resposta");
    }

    [Fact]
    public void Ignora_mensagens_nao_assistente_e_vazias()
    {
        var msgs = new[]
        {
            new ChatMessage(ChatRole.User, "pergunta do usuário"),
            new ChatMessage(ChatRole.Assistant, "   "),
            Assistant("resposta válida"),
        };

        WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Handoff, msgs)
            .Should().Be("resposta válida");
        // Só uma mensagem de assistente materializa → Concurrent também não concatena.
        WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Concurrent, msgs)
            .Should().Be("resposta válida");
    }

    [Fact]
    public void Lista_vazia_retorna_string_vazia()
    {
        WorkflowRunnerService.SelectAgentOutput(OrchestrationMode.Handoff, Array.Empty<ChatMessage>())
            .Should().BeEmpty();
    }
}
