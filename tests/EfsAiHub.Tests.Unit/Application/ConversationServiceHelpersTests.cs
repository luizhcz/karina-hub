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

    // ── CombineTrailingUserInputs — batch de user messages no mesmo turno ──

    [Fact]
    public void CombineTrailingUserInputs_UmaSoMensagem_RetornaInputOriginal()
    {
        var input = new ChatMessageInput("user", "Quero comprar", Actor.Human);
        var combined = ConversationService.CombineTrailingUserInputs(new[] { input });

        combined.Should().BeSameAs(input);
    }

    [Fact]
    public void CombineTrailingUserInputs_DuasUserConsecutivas_JuntaComDuasNewlines()
    {
        // Cenário: cliente envia ["Quero comprar um ativo", "petr4"] num batch.
        // Router só veria "petr4" antes — agora vê o turno combinado.
        var inputs = new[]
        {
            new ChatMessageInput("user", "Quero comprar um ativo", Actor.Human),
            new ChatMessageInput("user", "petr4", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Role.Should().Be("user");
        combined.Message.Should().Be("Quero comprar um ativo\n\npetr4");
    }

    [Fact]
    public void CombineTrailingUserInputs_TresUserConsecutivas_PreservaOrdem()
    {
        var inputs = new[]
        {
            new ChatMessageInput("user", "a", Actor.Human),
            new ChatMessageInput("user", "b", Actor.Human),
            new ChatMessageInput("user", "c", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Message.Should().Be("a\n\nb\n\nc");
    }

    [Fact]
    public void CombineTrailingUserInputs_AssistantNoMeio_SoCombinaTrailing()
    {
        // Cenário híbrido — caller deveria filtrar antes de chegar aqui, mas
        // o helper precisa ser robusto: só combina o run consecutivo no fim.
        var inputs = new[]
        {
            new ChatMessageInput("user", "ignored-old", Actor.Human),
            new ChatMessageInput("assistant", "resp", Actor.Human),
            new ChatMessageInput("user", "novo-1", Actor.Human),
            new ChatMessageInput("user", "novo-2", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Message.Should().Be("novo-1\n\nnovo-2");
    }

    [Fact]
    public void CombineTrailingUserInputs_UltimoNaoEhUser_RetornaUltimo()
    {
        // Edge case: trailing run sem user (só assistant/tool). Devolve o
        // último input intacto pra que callers superiores não branchem em null.
        var inputs = new[]
        {
            new ChatMessageInput("user", "u", Actor.Human),
            new ChatMessageInput("assistant", "a", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Role.Should().Be("assistant");
        combined.Message.Should().Be("a");
    }

    // ── ResolveHistoryWithEcho — echo só preenche quando DB vazio ─────────

    [Fact]
    public void ResolveHistoryWithEcho_DbVazioEEchoPresente_UsaEcho()
    {
        // Cenário: conv nova / synthetic. Cliente AG-UI manda
        // [user, assistant, user] num único call. DB ainda não tem nada.
        // Sem o echo, Router veria só o último user sem contexto dos turnos
        // anteriores que o cliente quer mostrar.
        var echo = new[]
        {
            new ChatMessageInput("user", "Quero comprar um ativo", Actor.Human),
            new ChatMessageInput("assistant", "Qual ativo?", Actor.Human),
        };

        var result = ConversationService.ResolveHistoryWithEcho(
            "conv-1",
            dbHistory: Array.Empty<ChatMessage>(),
            echoHistory: echo,
            out var applied);

        applied.Should().BeTrue();
        result.Should().HaveCount(2);
        result[0].Role.Should().Be("user");
        result[0].Content.Should().Be("Quero comprar um ativo");
        result[1].Role.Should().Be("assistant");
        result[1].Content.Should().Be("Qual ativo?");
        result[0].ConversationId.Should().Be("conv-1");
    }

    [Fact]
    public void ResolveHistoryWithEcho_DbComMensagens_IgnoraEcho()
    {
        // Cenário: conv estabelecida (turnos anteriores já persistidos).
        // DB é authoritative — preserva StructuredOutput dos assistants,
        // MessageId real, TokenCount, e impede divergência multi-tab.
        var dbHistory = new[]
        {
            ChatMessageOf("Quero comprar real", Actor.Human),
        };
        var echo = new[]
        {
            new ChatMessageInput("user", "spoofed prior", Actor.Human),
            new ChatMessageInput("assistant", "spoofed response", Actor.Human),
        };

        var result = ConversationService.ResolveHistoryWithEcho(
            "conv-1",
            dbHistory: dbHistory,
            echoHistory: echo,
            out var applied);

        applied.Should().BeFalse();
        result.Should().BeSameAs(dbHistory, "DB tem dados — echo é ignorado");
    }

    [Fact]
    public void ResolveHistoryWithEcho_EchoNullOuVazio_UsaDb()
    {
        var dbHistory = new[]
        {
            ChatMessageOf("real", Actor.Human),
        };

        var nullEcho = ConversationService.ResolveHistoryWithEcho(
            "conv-1", dbHistory, echoHistory: null, out var applied1);
        var emptyEcho = ConversationService.ResolveHistoryWithEcho(
            "conv-1", dbHistory, echoHistory: Array.Empty<ChatMessageInput>(), out var applied2);

        applied1.Should().BeFalse();
        applied2.Should().BeFalse();
        nullEcho.Should().BeSameAs(dbHistory);
        emptyEcho.Should().BeSameAs(dbHistory);
    }

    [Fact]
    public void ResolveHistoryWithEcho_DbVazioESemEcho_DevolveDbVazio()
    {
        var empty = Array.Empty<ChatMessage>();
        var result = ConversationService.ResolveHistoryWithEcho(
            "conv-1", empty, echoHistory: null, out var applied);

        applied.Should().BeFalse();
        result.Should().BeSameAs(empty);
    }

    [Fact]
    public void CombineTrailingUserInputs_UserAssistantUser_SoUltimoUserVira_Trigger()
    {
        // Padrão AG-UI canônico: cliente reenvia conversa inteira a cada call.
        // [user_old, assistant_old, user_new] → só user_new é nova; o resto
        // já está em DB (echo de histórico). Trigger combina só o trailing run
        // que neste caso é só user_new.
        var inputs = new[]
        {
            new ChatMessageInput("user", "Quero comprar PETR4", Actor.Human),
            new ChatMessageInput("assistant", "Quantas ações?", Actor.Human),
            new ChatMessageInput("user", "100", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Role.Should().Be("user");
        combined.Message.Should().Be("100");
    }

    [Fact]
    public void CombineTrailingUserInputs_RoleUserCaseInsensitive()
    {
        var inputs = new[]
        {
            new ChatMessageInput("USER", "a", Actor.Human),
            new ChatMessageInput("User", "b", Actor.Human),
        };

        var combined = ConversationService.CombineTrailingUserInputs(inputs);

        combined.Message.Should().Be("a\n\nb");
    }
}
