using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Host.Api.Services;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Application;

/// <summary>
/// Cobertura dos helpers internos do ConversationService que decidem persistência
/// e título de conversa em função de (Role, Actor). São pontos críticos do épico
/// actor=robot — qualquer regressão aqui pollui histórico ou nome de threads.
/// </summary>
public class ConversationServiceHelpersTests
{
    // ── BuildChatMessage — matriz (Role, Actor) ──────────────────────────────

    [Theory]
    [InlineData("user", Actor.Human, "user", Actor.Human)]
    [InlineData("USER", Actor.Human, "user", Actor.Human)]
    [InlineData("assistant", Actor.Human, "assistant", Actor.Human)]
    [InlineData("system", Actor.Human, "system", Actor.Human)]
    public void BuildChatMessage_RoleNormal_NaoSetaActorRobot(
        string inputRole, Actor inputActor, string expectedRole, Actor expectedActor)
    {
        var msg = ConversationService.BuildChatMessage(
            "conv-1",
            new ChatMessageInput(inputRole, "olá", inputActor));

        msg.Role.Should().Be(expectedRole);
        msg.Actor.Should().Be(expectedActor);
    }

    [Fact]
    public void BuildChatMessage_ActorRobotExplicito_PersistComoUserMaisRobot()
    {
        // Caminho novo (PR 2 vai usar): caller passa actor=Robot já tipado.
        var msg = ConversationService.BuildChatMessage(
            "conv-1",
            new ChatMessageInput("user", "{\"saldo\":12480}", Actor.Robot));

        msg.Role.Should().Be("user");
        msg.Actor.Should().Be(Actor.Robot);
        msg.Content.Should().Be("{\"saldo\":12480}");
    }

    [Theory]
    [InlineData("robot")]
    [InlineData("Robot")]
    [InlineData("ROBOT")]
    public void BuildChatMessage_RoleLegadoRobot_NaoVaiMaisProAssistant(string legacyRole)
    {
        // Bug fix: antes virava Role="assistant" e poluía histórico com fala-de-LLM falsa.
        // Hoje: Role="user" + Actor=Robot, preservando os 5 canônicos AG-UI.
        var msg = ConversationService.BuildChatMessage(
            "conv-1",
            new ChatMessageInput(legacyRole, "ordem confirmada"));

        msg.Role.Should().Be("user");
        msg.Actor.Should().Be(Actor.Robot);
    }

    // ── UpdateConversationTitle — robot não vira título ──────────────────────

    [Fact]
    public void UpdateConversationTitle_PrimeiraMensagemRobot_NaoUsaComoTitulo()
    {
        var conv = new ConversationSession
        {
            ConversationId = "conv-1",
            UserId = "u-1",
            WorkflowId = "wf-1"
        };
        var msgs = new[]
        {
            ChatMessageOf("{\"saldo\":12480}", Actor.Robot),
            ChatMessageOf("Posso transferir 5000?", Actor.Human)
        };

        ConversationService.UpdateConversationTitle(conv, msgs);

        conv.Title.Should().Be("Posso transferir 5000?");
    }

    [Fact]
    public void UpdateConversationTitle_SemMensagemHumana_NaoSetaTitulo()
    {
        // Edge case: conversa só com mensagens robot fica com Title=null.
        // Quando uma mensagem humana chegar depois, o título é preenchido.
        var conv = new ConversationSession
        {
            ConversationId = "conv-1",
            UserId = "u-1",
            WorkflowId = "wf-1"
        };
        var msgs = new[]
        {
            ChatMessageOf("{\"x\":1}", Actor.Robot),
            ChatMessageOf("{\"y\":2}", Actor.Robot)
        };

        ConversationService.UpdateConversationTitle(conv, msgs);

        conv.Title.Should().BeNull();
    }

    [Fact]
    public void UpdateConversationTitle_TituloJaDefinido_NaoSobrescreve()
    {
        var conv = new ConversationSession
        {
            ConversationId = "conv-1",
            UserId = "u-1",
            WorkflowId = "wf-1",
            Title = "título preservado"
        };
        var msgs = new[] { ChatMessageOf("primeira humana", Actor.Human) };

        ConversationService.UpdateConversationTitle(conv, msgs);

        conv.Title.Should().Be("título preservado");
    }

    private static ChatMessage ChatMessageOf(string content, Actor actor) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        ConversationId = "conv-1",
        Role = "user",
        Content = content,
        Actor = actor
    };
}
