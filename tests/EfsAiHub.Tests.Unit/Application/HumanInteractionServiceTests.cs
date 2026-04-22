using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Coordination;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class HumanInteractionServiceTests
{
    private static HumanInteractionService Build(IHumanInteractionRepository? repo = null)
    {
        repo ??= Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        return new HumanInteractionService(
            repo,
            Substitute.For<ILogger<HumanInteractionService>>());
    }

    private static HumanInteractionRequest NewRequest(string id = "int-1", string execId = "exec-1") =>
        new()
        {
            InteractionId = id,
            ExecutionId = execId,
            WorkflowId = "wf-1",
            Prompt = "Confirma a operação?"
        };

    [Fact]
    public async Task LoadPendingFromDb_PopulaCache()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-loaded");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        svc.GetById("int-loaded").Should().NotBeNull();
    }

    [Fact]
    public async Task LoadPendingFromDb_CacheVazio_NaoLoga()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        svc.GetPending().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_IdInexistente_RetornaFalseSemCAS()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest>());

        var svc = Build(repo);

        var result = await svc.ResolveAsync("nao-existe", "sim", resolvedBy: "user-123");

        result.Should().BeFalse();
        // Nem deveria chegar a tentar CAS se o id não está em _pending
        await repo.DidNotReceive().TryResolveAsync(
            Arg.Any<string>(), Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_CASVence_AprovadaERemovidaDoCache()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-b", "exec-b");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });
        repo.TryResolveAsync(
            Arg.Any<string>(),
            Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);  // CAS vence

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = await svc.ResolveAsync("int-b", "aprovado", resolvedBy: "user-b", approved: true, publishToCross: false);

        result.Should().BeTrue();
        svc.GetById("int-b").Should().BeNull(); // removido do cache local
    }

    [Fact]
    public async Task Resolve_CASVence_PropagaResolvedByParaRepo()
    {
        // Garante que o userId passado à service chega ao repo.TryResolveAsync.
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-rb", "exec-rb");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });
        repo.TryResolveAsync(
            Arg.Any<string>(),
            Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        await svc.ResolveAsync("int-rb", "ok", resolvedBy: "operator-42", publishToCross: false);

        await repo.Received(1).TryResolveAsync(
            "int-rb",
            Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            "ok",
            Arg.Any<DateTime>(),
            "operator-42",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_CASPerde_RetornaFalseELimpaLocal()
    {
        // Outro pod já resolveu — CAS retorna false. Esperado: service retorna false
        // mas ainda limpa estado local (senão o pending ficaria orfão no cache).
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-conflict", "exec-conflict");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });
        repo.TryResolveAsync(
            Arg.Any<string>(),
            Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);  // Outro venceu o CAS

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = await svc.ResolveAsync("int-conflict", "aprovado", resolvedBy: "user-c", approved: true, publishToCross: false);

        result.Should().BeFalse();
        svc.GetById("int-conflict").Should().BeNull(); // limpeza local acontece mesmo em conflict
    }

    [Fact]
    public async Task Resolve_ChamadasConcorrentes_ApenasUmaRetornaTrue()
    {
        // Race test: 50 callers tentam resolver simultaneamente. CAS garante que apenas 1 vence;
        // os outros 49 recebem false sem corromper estado em memória.
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req = NewRequest("int-race", "exec-race");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req });

        // Semantics reais do banco: primeiro rowsAffected=1 vira 0 pra todos os seguintes.
        var casCalls = 0;
        repo.TryResolveAsync(
            Arg.Any<string>(),
            Arg.Any<EfsAiHub.Core.Orchestration.Enums.HumanInteractionStatus>(),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref casCalls) == 1);

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () =>
                await svc.ResolveAsync("int-race", "approve", resolvedBy: "user-race", approved: true, publishToCross: false)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1, "apenas uma chamada deve vencer o CAS");
        results.Count(r => !r).Should().Be(49, "todas as outras devem receber false");
    }

    [Fact]
    public void GetPendingForExecution_SemItens_RetornaNull()
    {
        var svc = Build();

        var pending = svc.GetPendingForExecution("exec-xyz");

        pending.Should().BeNull();
    }

    [Fact]
    public async Task GetByExecutionId_RetornaInteracoesDaExecucao()
    {
        var repo = Substitute.For<IHumanInteractionRepository>();
        var req1 = NewRequest("int-1", "exec-same");
        var req2 = NewRequest("int-2", "exec-same");
        var req3 = NewRequest("int-3", "exec-other");
        repo.GetPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new List<HumanInteractionRequest> { req1, req2, req3 });

        var svc = new HumanInteractionService(repo, Substitute.For<ILogger<HumanInteractionService>>());
        await svc.LoadPendingFromDbAsync();

        var result = svc.GetByExecutionId("exec-same");

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.ExecutionId == "exec-other");
    }
}
