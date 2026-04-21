using System.Text.Json;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Platform.Runtime;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class ChatTurnContextMapperTests
{
    private static string BuildCtxJson(
        string message = "Olá",
        string userId = "u-1",
        string conversationId = "conv-1",
        List<ChatTurnMessage>? history = null,
        Dictionary<string, string>? metadata = null)
    {
        var ctx = new ChatTurnContext
        {
            UserId = userId,
            ConversationId = conversationId,
            Message = new ChatTurnMessage { Role = "user", Content = message },
            History = history ?? [],
            Metadata = metadata ?? new Dictionary<string, string> { ["workflowId"] = "wf-1" }
        };
        return JsonSerializer.Serialize(ctx);
    }

    [Fact]
    public void Build_ModoGraph_RetornaJsonBlobComoUserMessage()
    {
        var json = BuildCtxJson("minha pergunta");

        var messages = ChatTurnContextMapper.Build(json, OrchestrationMode.Graph);

        messages.Should().HaveCount(1);
        messages[0].Role.Should().Be(ChatRole.User);
        messages[0].Text.Should().Be(json);
    }

    [Fact]
    public void Build_ModoSequential_RetornaJsonBlobComoUserMessage()
    {
        var json = BuildCtxJson("pergunta seq");

        var messages = ChatTurnContextMapper.Build(json, OrchestrationMode.Sequential);

        messages.Should().HaveCount(1);
        messages[0].Text.Should().Be(json);
    }

    [Fact]
    public void Build_ModoHandoff_ExpandeContexto()
    {
        var json = BuildCtxJson("qual a cotação?");

        var messages = ChatTurnContextMapper.Build(json, OrchestrationMode.Handoff);

        // Deve ter: system(metadata) + user(message) + system(json blob) = pelo menos 3
        messages.Should().HaveCountGreaterThan(2);
        messages.Should().Contain(m => m.Role == ChatRole.User && m.Text == "qual a cotação?");
    }

    [Fact]
    public void Build_ModoHandoff_IncluiMetadataComoSystem()
    {
        var json = BuildCtxJson(metadata: new Dictionary<string, string> { ["userId"] = "u-xyz", ["tema"] = "trading" });

        var messages = ChatTurnContextMapper.Build(json, OrchestrationMode.Handoff);

        messages.Should().Contain(m => m.Role == ChatRole.System && m.Text!.Contains("userId"));
    }

    [Fact]
    public void Build_ModoHandoff_IncluiHistorico()
    {
        var history = new List<ChatTurnMessage>
        {
            new() { Role = "user", Content = "pergunta anterior" },
            new() { Role = "assistant", Content = "resposta anterior" }
        };
        var json = BuildCtxJson(history: history);

        var messages = ChatTurnContextMapper.Build(json, OrchestrationMode.Handoff);

        messages.Should().Contain(m => m.Role == ChatRole.User && m.Text == "pergunta anterior");
        messages.Should().Contain(m => m.Role == ChatRole.Assistant && m.Text == "resposta anterior");
    }

    [Fact]
    public void Build_InputVazio_RetornaUmaUserMessageVazia()
    {
        var messages = ChatTurnContextMapper.Build(null, OrchestrationMode.Sequential);

        messages.Should().HaveCount(1);
        messages[0].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public void TryExpand_JsonComMetadata_RetornaMensagens()
    {
        var json = BuildCtxJson("qual o saldo?");

        var messages = ChatTurnContextMapper.TryExpand(json);

        messages.Should().NotBeNull();
        messages.Should().Contain(m => m.Text == "qual o saldo?");
    }

    [Fact]
    public void TryExpand_StringSimples_RetornaNull()
    {
        var messages = ChatTurnContextMapper.TryExpand("apenas texto simples");

        messages.Should().BeNull();
    }

    [Fact]
    public void TryExpand_Null_RetornaNull()
    {
        var messages = ChatTurnContextMapper.TryExpand(null);

        messages.Should().BeNull();
    }

    [Fact]
    public void TryExpand_JsonSemMetadata_RetornaNull()
    {
        var ctx = new ChatTurnContext
        {
            UserId = "u-1",
            ConversationId = "c-1",
            Message = new ChatTurnMessage { Role = "user", Content = "oi" },
            Metadata = []
        };
        var json = JsonSerializer.Serialize(ctx);

        var messages = ChatTurnContextMapper.TryExpand(json);

        messages.Should().BeNull();
    }
}
