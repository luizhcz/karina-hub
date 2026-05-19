using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Host.Api.AgentSandbox;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.AgentSandbox;

[Trait("Category", "Unit")]
public class AgentSandboxServiceTests
{
    private const string CallerId = "admin-tester";

    private static AgentDefinition StubAgent(string id, AgentType type, bool enabled = true) => new()
    {
        Id = id,
        Name = id,
        Type = type,
        Enabled = enabled,
        Model = new AgentModelConfig { DeploymentName = "gpt-4" },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI", ClientType = "ChatCompletion" },
        ProjectId = "default",
        TenantId = "default",
        Visibility = "project",
    };

    private static AgentVersion StubVersion(string agentId, string versionId, int revision = 1) =>
        new(
            AgentVersionId: versionId,
            AgentDefinitionId: agentId,
            Revision: revision,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: null,
            ChangeReason: null,
            Status: AgentVersionStatus.Published,
            PromptContent: null,
            PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4", null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, true),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: Array.Empty<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: "h",
            Description: null,
            Metadata: null,
            FallbackProvider: null,
            Tools: null,
            BreakingChange: false);

    private static (AgentSandboxService svc,
                    IAgentDefinitionRepository agentRepo,
                    IAgentVersionRepository versionRepo,
                    IAgentSandboxSessionRepository sessionRepo,
                    IWorkflowService workflowSvc,
                    IConversationLifecycle convLifecycle,
                    IChatMessageRepository messageRepo)
        BuildService(params AgentDefinition[] agents)
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        foreach (var a in agents)
        {
            agentRepo.GetByIdAsync(a.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AgentDefinition?>(a));
        }
        agentRepo.SetChatSandboxValidationAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var versionRepo = Substitute.For<IAgentVersionRepository>();
        versionRepo.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<AgentVersion?>(StubVersion(call.ArgAt<string>(0), "v-current")));

        var sessionRepo = Substitute.For<IAgentSandboxSessionRepository>();
        sessionRepo.CreateAsync(Arg.Any<AgentSandboxSession>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<AgentSandboxSession>()));
        sessionRepo.UpdateAsync(Arg.Any<AgentSandboxSession>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<AgentSandboxSession>()));

        var workflowSvc = Substitute.For<IWorkflowService>();
        workflowSvc.CreateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<WorkflowDefinition>()));

        var convLifecycle = Substitute.For<IConversationLifecycle>();
        convLifecycle.CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ConversationSession
            {
                ConversationId = "conv-test",
                UserId = call.ArgAt<string>(1),
                UserType = call.ArgAt<string>(2),
                WorkflowId = call.ArgAt<string>(0),
            }));

        var messageRepo = Substitute.For<IChatMessageRepository>();
        // Default: 0 mensagens. Testes que precisam de turn registrado fazem
        // override do retorno antes da chamada.
        messageRepo.ListAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<EfsAiHub.Core.Abstractions.Conversations.ChatMessage>)
                Array.Empty<EfsAiHub.Core.Abstractions.Conversations.ChatMessage>()));

        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext("sales-trader-ai", isExplicit: true));

        var options = Options.Create(new AgentSandboxOptions { SessionTtlDays = 7 });
        var logger = Substitute.For<ILogger<AgentSandboxService>>();

        var svc = new AgentSandboxService(
            sessionRepo,
            agentRepo,
            workflowSvc,
            convLifecycle,
            messageRepo,
            projectAccessor,
            options,
            logger,
            agentVersionRepo: versionRepo);

        return (svc, agentRepo, versionRepo, sessionRepo, workflowSvc, convLifecycle, messageRepo);
    }

    private static UserContext Caller() => new(CallerId, "admin");

    [Fact]
    public async Task CreateSessionAsync_RejectsRouter()
    {
        var (svc, _, _, _, workflowSvc, _, _) = BuildService(StubAgent("router-1", AgentType.Router));

        var act = async () => await svc.CreateSessionAsync("router-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("predict-intent");
        await workflowSvc.DidNotReceive().CreateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSessionAsync_RejectsDisabledAgent()
    {
        var (svc, _, _, _, _, _, _) = BuildService(StubAgent("agent-1", AgentType.Conversational, enabled: false));

        var act = async () => await svc.CreateSessionAsync("agent-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("desabilitado");
    }

    [Fact]
    public async Task CreateSessionAsync_RejectsMissingAgent()
    {
        var (svc, _, _, _, _, _, _) = BuildService();

        var act = async () => await svc.CreateSessionAsync("missing", Caller(), null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateSessionAsync_BuildsChatWorkflowWithExactPin()
    {
        var (svc, _, _, _, workflowSvc, _, _) = BuildService(StubAgent("conv-1", AgentType.Conversational));

        var session = await svc.CreateSessionAsync("conv-1", Caller(), null);

        // Workflow Chat criado: InputMode=Chat, Graph, 1 agent, deploymentKind=chat, kind=chat-sandbox.
        // A chave `chatSandboxSessionId` é a authority pra que o frontend
        // (ChatDeploymentSandbox.tsx) detecte que é session de sandbox e
        // habilite o botão "Marcar como validado". Trocar a chave quebra o gate
        // silenciosamente — esta asserção previne regressão.
        await workflowSvc.Received(1).CreateAsync(
            Arg.Is<WorkflowDefinition>(w =>
                w.Configuration.InputMode == "Chat"
                && w.OrchestrationMode == OrchestrationMode.Graph
                && w.Agents.Count == 1
                && w.Agents[0].AgentId == "conv-1"
                && w.Agents[0].AgentVersionId == "v-current"
                && w.Metadata!["deploymentKind"] == "chat"
                && w.Metadata["kind"] == "chat-sandbox"
                && w.Metadata["transient"] == "true"
                && w.Metadata.ContainsKey("chatSandboxSessionId")
                && !string.IsNullOrEmpty(w.Metadata["chatSandboxSessionId"])),
            Arg.Any<CancellationToken>());

        session.AgentId.Should().Be("conv-1");
        session.AgentVersionId.Should().Be("v-current");
        session.Mode.Should().Be(AgentSandboxModes.Chat);
        session.Status.Should().Be(AgentSandboxSessionStatus.Active);
        session.CreatedByUserId.Should().Be(CallerId);
        session.ConversationId.Should().Be("conv-test");
    }

    [Fact]
    public async Task CreateSessionAsync_HonorsExplicitAgentVersionId()
    {
        var (svc, _, versionRepo, _, _, _, _) = BuildService(StubAgent("conv-1", AgentType.Conversational));
        versionRepo.GetByIdAsync("v-explicit", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("conv-1", "v-explicit", revision: 2)));

        var session = await svc.CreateSessionAsync(
            "conv-1",
            Caller(),
            new AgentSandboxService.CreateSessionRequest("v-explicit"));

        session.AgentVersionId.Should().Be("v-explicit");
    }

    [Fact]
    public async Task CreateSessionAsync_RejectsExplicitVersionFromDifferentAgent()
    {
        var (svc, _, versionRepo, _, _, _, _) = BuildService(StubAgent("conv-1", AgentType.Conversational));
        versionRepo.GetByIdAsync("v-other", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentVersion?>(StubVersion("other-agent", "v-other")));

        var act = async () => await svc.CreateSessionAsync(
            "conv-1",
            Caller(),
            new AgentSandboxService.CreateSessionRequest("v-other"));

        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.Message.Should().Contain("não pertence");
    }

    [Theory]
    [InlineData(AgentType.Custom)]
    [InlineData(AgentType.Worker)]
    [InlineData(AgentType.ToolRunner)]
    public async Task CreateSessionAsync_BuildsStandaloneWorkflowForNonChatTypes(AgentType type)
    {
        var (svc, _, _, _, workflowSvc, convLifecycle, _) = BuildService(StubAgent("a-1", type));

        var session = await svc.CreateSessionAsync("a-1", Caller(), null);

        // Workflow Standalone: InputMode=Standalone, Sequential, kind=standalone-sandbox.
        // Carrega chave chatSandboxSessionId no metadata (compat com leitores legacy).
        await workflowSvc.Received(1).CreateAsync(
            Arg.Is<WorkflowDefinition>(w =>
                w.Configuration.InputMode == "Standalone"
                && w.OrchestrationMode == OrchestrationMode.Sequential
                && w.Agents.Count == 1
                && w.Agents[0].AgentId == "a-1"
                && w.Metadata!["deploymentKind"] == "standalone"
                && w.Metadata["kind"] == "standalone-sandbox"
                && w.Metadata["transient"] == "true"
                && w.Metadata["deployedFromAgentType"] == type.ToString()
                && w.Metadata.ContainsKey("chatSandboxSessionId")),
            Arg.Any<CancellationToken>());

        // Standalone NÃO cria conversation — fluxo single-shot via SSE de execução.
        await convLifecycle.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>());

        session.Mode.Should().Be(AgentSandboxModes.Standalone);
        session.ConversationId.Should().BeNull();
        session.Status.Should().Be(AgentSandboxSessionStatus.Active);
    }

    [Fact]
    public async Task CreateSessionAsync_RejectsDisabledAgentEvenForStandalone()
    {
        var (svc, _, _, _, workflowSvc, _, _) = BuildService(StubAgent("w-1", AgentType.Worker, enabled: false));

        var act = async () => await svc.CreateSessionAsync("w-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("desabilitado");
        await workflowSvc.DidNotReceive().CreateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseSessionAsync_RefusesToCloseValidatedSession()
    {
        var (svc, _, _, sessionRepo, _, _, _) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(new AgentSandboxSession
            {
                SandboxSessionId = "s-1",
                AgentId = "a", AgentVersionId = "v", WorkflowId = "w", ConversationId = "c",
                Mode = AgentSandboxModes.Chat,
                ProjectId = "sales-trader-ai", CreatedByUserId = "u",
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
                Status = AgentSandboxSessionStatus.Validated,
            }));

        var act = async () => await svc.CloseSessionAsync("s-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CloseSessionAsync_DeletesWorkflowImmediately()
    {
        var (svc, _, _, sessionRepo, workflowSvc, _, _) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession()));

        await svc.CloseSessionAsync("s-1");

        // Update vem antes do Delete: se o delete crashar, session já está
        // marcada Closed e cleanup background cobre o workflow órfão.
        Received.InOrder(() =>
        {
            sessionRepo.UpdateAsync(
                Arg.Is<AgentSandboxSession>(s => s.Status == AgentSandboxSessionStatus.Closed),
                Arg.Any<CancellationToken>());
            workflowSvc.DeleteAsync("deploy-chat-sandbox-x", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task CloseSessionAsync_SwallowsWorkflowDeleteFailure()
    {
        var (svc, _, _, sessionRepo, workflowSvc, _, _) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession()));
        workflowSvc.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = async () => await svc.CloseSessionAsync("s-1");

        await act.Should().NotThrowAsync();
        await sessionRepo.Received(1).UpdateAsync(
            Arg.Is<AgentSandboxSession>(s => s.Status == AgentSandboxSessionStatus.Closed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseSessionAsync_NoOpWhenAlreadyClosed()
    {
        var (svc, _, _, sessionRepo, workflowSvc, _, _) = BuildService();
        var closed = StubActiveSession();
        closed.Status = AgentSandboxSessionStatus.Closed;
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(closed));

        await svc.CloseSessionAsync("s-1");

        await workflowSvc.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sessionRepo.DidNotReceive().UpdateAsync(Arg.Any<AgentSandboxSession>(), Arg.Any<CancellationToken>());
    }

    private static AgentSandboxSession StubActiveSession(string mode = AgentSandboxModes.Chat) => new()
    {
        SandboxSessionId = "s-1",
        AgentId = "conv-1",
        AgentVersionId = "v-pinned",
        Mode = mode,
        WorkflowId = "deploy-chat-sandbox-x",
        ConversationId = mode == AgentSandboxModes.Chat ? "conv-1-thread" : null,
        ProjectId = "sales-trader-ai",
        CreatedByUserId = "owner",
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        Status = AgentSandboxSessionStatus.Active,
    };

    private static EfsAiHub.Core.Abstractions.Conversations.ChatMessage StubMessage() => new()
    {
        MessageId = "m-1",
        ConversationId = "conv-1-thread",
        Role = "user",
        Content = "hi",
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ValidateAsync_RejectsSessionNotFound()
    {
        var (svc, _, _, _, _, _, _) = BuildService();

        var act = async () => await svc.ValidateAsync("nope", Caller(), null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ValidateAsync_RejectsNonActiveSession()
    {
        var (svc, _, _, sessionRepo, _, _, _) = BuildService();
        var session = StubActiveSession();
        session.Status = AgentSandboxSessionStatus.Closed;
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(session));

        var act = async () => await svc.ValidateAsync("s-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("Active");
    }

    [Fact]
    public async Task ValidateAsync_RejectsSessionWithoutMessages()
    {
        var (svc, _, _, sessionRepo, _, _, _) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession()));

        var act = async () => await svc.ValidateAsync("s-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("sem mensagens");
    }

    [Fact]
    public async Task ValidateAsync_RejectsStandaloneSession()
    {
        var (svc, _, _, sessionRepo, _, _, _) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession(AgentSandboxModes.Standalone)));

        var act = async () => await svc.ValidateAsync("s-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("Chat");
    }

    [Fact]
    public async Task ValidateAsync_PopulatesAgentValidationAndUpdatesSession()
    {
        var (svc, agentRepo, _, sessionRepo, _, _, messageRepo) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession()));
        messageRepo.ListAsync("conv-1-thread", 1, 0, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<EfsAiHub.Core.Abstractions.Conversations.ChatMessage>)
                new[] { StubMessage() }));

        var result = await svc.ValidateAsync("s-1", Caller(), new AgentSandboxService.ValidateSessionRequest("smoke"));

        result.Status.Should().Be(AgentSandboxSessionStatus.Validated);
        result.ValidatedByUserId.Should().Be(CallerId);
        result.ValidationNotes.Should().Be("smoke");

        await agentRepo.Received(1).SetChatSandboxValidationAsync(
            "conv-1",
            Arg.Any<DateTime>(),
            CallerId,
            "v-pinned",
            Arg.Any<CancellationToken>());
        await sessionRepo.Received(1).UpdateAsync(
            Arg.Is<AgentSandboxSession>(s => s.Status == AgentSandboxSessionStatus.Validated
                                            && s.ValidatedByUserId == CallerId),
            Arg.Any<CancellationToken>());
    }

    // ── TryParseRouterOutput ─────────────────────────────────────────────────
    // Parser é internal e isolado do agente — testar diretamente evita mock
    // pesado de IAgentFactory + LLM pra cobrir os edge cases que importam.

    [Fact]
    public void TryParseRouterOutput_ValidJson_ExtractsIntentAndReasoning()
    {
        var (intent, reasoning) = AgentSandboxService.TryParseRouterOutput(
            "{\"intent\":\"billing\",\"reasoning\":\"user mentioned invoice\"}");

        intent.Should().Be("billing");
        reasoning.Should().Be("user mentioned invoice");
    }

    [Fact]
    public void TryParseRouterOutput_MalformedJson_ReturnsUnknown()
    {
        var (intent, reasoning) = AgentSandboxService.TryParseRouterOutput(
            "intent: billing, not really json");

        intent.Should().Be("unknown");
        reasoning.Should().BeNull();
    }

    [Fact]
    public void TryParseRouterOutput_JsonWithoutIntentField_ReturnsUnknown()
    {
        var (intent, reasoning) = AgentSandboxService.TryParseRouterOutput(
            "{\"category\":\"support\",\"score\":0.9}");

        intent.Should().Be("unknown");
        reasoning.Should().BeNull();
    }

    [Fact]
    public void TryParseRouterOutput_EmptyInput_ReturnsUnknown()
    {
        var (intent, _) = AgentSandboxService.TryParseRouterOutput("");
        intent.Should().Be("unknown");
    }

    [Fact]
    public void TryParseRouterOutput_JsonObjectWithIntentNoReasoning_ReturnsNullReasoning()
    {
        var (intent, reasoning) = AgentSandboxService.TryParseRouterOutput(
            "{\"intent\":\"sales\"}");

        intent.Should().Be("sales");
        reasoning.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_FailsWhenAgentMissing()
    {
        var (svc, agentRepo, _, sessionRepo, _, _, messageRepo) = BuildService();
        sessionRepo.GetByIdAsync("s-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AgentSandboxSession?>(StubActiveSession()));
        messageRepo.ListAsync("conv-1-thread", 1, 0, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((IReadOnlyList<EfsAiHub.Core.Abstractions.Conversations.ChatMessage>)
                new[] { StubMessage() }));
        agentRepo.SetChatSandboxValidationAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var act = async () => await svc.ValidateAsync("s-1", Caller(), null);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("não foi encontrado");
    }
}
