using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents.Interfaces;
using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

/// <summary>
/// Cobre o helper EnsureExecutionContext do AgentSessionService (PR 10.a / IsExplicit guard).
/// Testes diretos via internal visibility (Platform.Runtime → Tests.Unit).
/// Não testa RunAsync/RunStreamingAsync end-to-end — esses dependem de IAgentFactory real
/// (cria AIAgent com provider) e ficam pra integration tests.
/// </summary>
[Trait("Category", "Unit")]
public class AgentSessionServiceTests
{
    private static AgentSessionService Build(IProjectContextAccessor accessor)
    {
        return new AgentSessionService(
            sessionStore: Substitute.For<IAgentSessionStore>(),
            agentRepo: Substitute.For<IAgentDefinitionRepository>(),
            agentFactory: Substitute.For<IAgentFactory>(),
            projectContext: accessor,
            logger: Substitute.For<ILogger<AgentSessionService>>());
    }

    private static AgentSessionRecord MakeRecord() => new()
    {
        SessionId = $"sess-{Guid.NewGuid():N}",
        AgentId = "advisor",
        SerializedState = JsonDocument.Parse("{}").RootElement,
        TurnCount = 0
    };

    private static IProjectContextAccessor MakeAccessor(ProjectContext ctx)
    {
        var accessor = Substitute.For<IProjectContextAccessor>();
        accessor.Current.Returns(ctx);
        return accessor;
    }

    [Fact]
    public void EnsureExecutionContext_QuandoIsExplicitFalse_LancaInvalidOperation()
    {
        // ProjectContext.Default tem IsExplicit=false — sentinel pra "ninguém populou".
        var svc = Build(MakeAccessor(ProjectContext.Default));
        var record = MakeRecord();

        var act = () => svc.EnsureExecutionContext(record, "msg");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProjectMiddleware*");
    }

    [Fact]
    public void EnsureExecutionContext_QuandoCriadoSemMiddleware_LancaTambem()
    {
        // Cenário: dev cria ProjectContext direto com isExplicit=false explicitamente.
        var ctx = new ProjectContext("any-id", isExplicit: false);
        var svc = Build(MakeAccessor(ctx));

        var act = () => svc.EnsureExecutionContext(MakeRecord(), "msg");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureExecutionContext_QuandoIsExplicitTrue_PopulaExecutionContext()
    {
        var ctx = new ProjectContext("my-project", projectName: "My", isExplicit: true);
        var svc = Build(MakeAccessor(ctx));
        var record = MakeRecord();

        try
        {
            svc.EnsureExecutionContext(record, "test message");

            DelegateExecutor.Current.Value.Should().NotBeNull();
            DelegateExecutor.Current.Value!.ProjectId.Should().Be("my-project");
            DelegateExecutor.Current.Value.ExecutionId.Should().Be(record.SessionId);
            DelegateExecutor.Current.Value.WorkflowId.Should().Be($"agent-session:{record.AgentId}");
            DelegateExecutor.Current.Value.Input.Should().Be("test message");
        }
        finally
        {
            // Cleanup do AsyncLocal pra não vazar entre testes.
            DelegateExecutor.Current.Value = null;
        }
    }

    [Fact]
    public void EnsureExecutionContext_FallbackHttp_ProjectIdDefaultMasIsExplicitTrue_NaoLanca()
    {
        // Cenário legítimo: HTTP request sem header x-efs-project-id. ProjectMiddleware caiu no fallback,
        // setou accessor.Current = new ProjectContext("default", isExplicit: true). Não deve lançar.
        var ctx = new ProjectContext("default", projectName: "Default", isExplicit: true);
        var svc = Build(MakeAccessor(ctx));

        try
        {
            var act = () => svc.EnsureExecutionContext(MakeRecord(), "msg");
            act.Should().NotThrow();
            DelegateExecutor.Current.Value!.ProjectId.Should().Be("default");
        }
        finally
        {
            DelegateExecutor.Current.Value = null;
        }
    }

    [Fact]
    public void EnsureExecutionContext_BudgetEhZero_SemEnforcement()
    {
        // Standalone session não configura cap de tokens. Budget=0 sinaliza "sem enforcement"
        // pro TokenTrackingChatClient (IsExceeded=false sempre).
        var svc = Build(MakeAccessor(new ProjectContext("p1", isExplicit: true)));

        try
        {
            svc.EnsureExecutionContext(MakeRecord(), "msg");
            var ctx = DelegateExecutor.Current.Value!;
            ctx.Budget.MaxTokensPerExecution.Should().Be(0);
            ctx.Budget.IsExceeded.Should().BeFalse();
        }
        finally
        {
            DelegateExecutor.Current.Value = null;
        }
    }

    [Fact]
    public void EnsureExecutionContext_GuardModeNoneEUserIdNull_StandaloneSemAccountGuard()
    {
        var svc = Build(MakeAccessor(new ProjectContext("p1", isExplicit: true)));

        try
        {
            svc.EnsureExecutionContext(MakeRecord(), "msg");
            var ctx = DelegateExecutor.Current.Value!;
            ctx.GuardMode.Should().Be(EfsAiHub.Core.Agents.Execution.AccountGuardMode.None);
            ctx.UserId.Should().BeNull();
        }
        finally
        {
            DelegateExecutor.Current.Value = null;
        }
    }
}
