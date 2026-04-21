using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Tests.Unit.AgUi;

/// <summary>
/// Testa a lógica de resolução do input AG-UI:
/// extração de effectiveMessage, detecção de HITL puro e resolução de workflowId.
/// Essas regras vivem em AgUiEndpoints.StreamAsync — testadas aqui como funções puras.
/// </summary>
[Trait("Category", "Unit")]
public class AgUiRunInputTests
{
    // Reproduz a lógica de resolução de effectiveMessage do StreamAsync
    private static string? ResolveEffectiveMessage(IReadOnlyList<AgUiInputMessage>? messages)
        => messages?.LastOrDefault(m => m.Role == "user")?.Content;

    // Reproduz a lógica isHitlPure
    private static bool IsHitlPure(IReadOnlyList<AgUiInputMessage>? messages)
    {
        var effectiveMessage = ResolveEffectiveMessage(messages);
        var hasToolMessages = messages?.Any(m => m.Role == "tool") ?? false;
        return string.IsNullOrWhiteSpace(effectiveMessage) && hasToolMessages;
    }

    // Reproduz a resolução de workflowId: body tem prioridade
    private static string? ResolveWorkflowId(string? bodyWorkflowId, string? headerValue)
        => bodyWorkflowId ?? headerValue;

    [Fact]
    public void Messages_Null_EffectiveMessageNulo()
    {
        var msg = ResolveEffectiveMessage(null);

        msg.Should().BeNull();
    }

    [Fact]
    public void Messages_ApenasToolRole_EffectiveMessageNulo()
    {
        var messages = new[]
        {
            new AgUiInputMessage("tool", "aprovado", "t-1")
        };

        var msg = ResolveEffectiveMessage(messages);

        msg.Should().BeNull();
    }

    [Fact]
    public void Messages_ComUserRole_RetornaMensagem()
    {
        var messages = new[]
        {
            new AgUiInputMessage("user", "minha pergunta")
        };

        var msg = ResolveEffectiveMessage(messages);

        msg.Should().Be("minha pergunta");
    }

    [Fact]
    public void Messages_UltimaMensagemUser_Retornada()
    {
        var messages = new[]
        {
            new AgUiInputMessage("user", "primeira"),
            new AgUiInputMessage("assistant", "resposta"),
            new AgUiInputMessage("user", "segunda")
        };

        var msg = ResolveEffectiveMessage(messages);

        msg.Should().Be("segunda");
    }

    [Fact]
    public void IsHitlPure_ToolSemUser_RetornaTrue()
    {
        var messages = new[]
        {
            new AgUiInputMessage("tool", "aprovado", "int-1")
        };

        IsHitlPure(messages).Should().BeTrue();
    }

    [Fact]
    public void IsHitlPure_ComUserMessage_RetornaFalse()
    {
        var messages = new[]
        {
            new AgUiInputMessage("user", "nova pergunta"),
            new AgUiInputMessage("tool", "aprovado", "int-1")
        };

        IsHitlPure(messages).Should().BeFalse();
    }

    [Fact]
    public void IsHitlPure_SemMensagens_RetornaFalse()
    {
        IsHitlPure(null).Should().BeFalse();
    }

    [Fact]
    public void WorkflowId_Body_TemPrioridade()
    {
        var workflowId = ResolveWorkflowId("wf-body", "wf-header");

        workflowId.Should().Be("wf-body");
    }

    [Fact]
    public void WorkflowId_BodyNull_UsaHeader()
    {
        var workflowId = ResolveWorkflowId(null, "wf-header");

        workflowId.Should().Be("wf-header");
    }

    [Fact]
    public void WorkflowId_AmbosNull_RetornaNull()
    {
        var workflowId = ResolveWorkflowId(null, null);

        workflowId.Should().BeNull();
    }
}
