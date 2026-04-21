using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class LlmTokenUsageRepositoryTests(IntegrationWebApplicationFactory factory)
{
    private ILlmTokenUsageRepository Repo =>
        factory.Services.GetRequiredService<ILlmTokenUsageRepository>();

    private static LlmTokenUsage MakeUsage(
        string? agentId = null,
        string? executionId = null,
        int inputTokens = 100,
        int outputTokens = 50,
        DateTime? createdAt = null)
    {
        var usage = new LlmTokenUsage
        {
            AgentId = agentId ?? $"agent-{Guid.NewGuid():N}",
            ModelId = "gpt-4o",
            ExecutionId = executionId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            DurationMs = 250
        };
        if (createdAt.HasValue)
            usage.CreatedAt = createdAt.Value;
        return usage;
    }

    // ── Append + GetByExecutionId ─────────────────────────────────────────────

    [Fact]
    public async Task AppendAsync_GetByExecutionId_RetornaLinhaComTokensCorretos()
    {
        var executionId = Guid.NewGuid().ToString();
        var usage = MakeUsage(executionId: executionId, inputTokens: 70, outputTokens: 30);

        await Repo.AppendAsync(usage);
        var results = await Repo.GetByExecutionIdAsync(executionId);

        results.Should().ContainSingle();
        var row = results[0];
        row.InputTokens.Should().Be(70);
        row.OutputTokens.Should().Be(30);
        row.TotalTokens.Should().Be(100);
        row.ExecutionId.Should().Be(executionId);
    }

    [Fact]
    public async Task GetByExecutionId_Inexistente_RetornaListaVazia()
    {
        var results = await Repo.GetByExecutionIdAsync(Guid.NewGuid().ToString());

        results.Should().BeEmpty();
    }

    // ── Batch AppendAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AppendAsync_MultiplasChamadas_TodasPersistidas()
    {
        var executionId = Guid.NewGuid().ToString();

        await Repo.AppendAsync(MakeUsage(executionId: executionId, inputTokens: 10, outputTokens: 5));
        await Repo.AppendAsync(MakeUsage(executionId: executionId, inputTokens: 20, outputTokens: 10));
        await Repo.AppendAsync(MakeUsage(executionId: executionId, inputTokens: 30, outputTokens: 15));

        var results = await Repo.GetByExecutionIdAsync(executionId);

        results.Should().HaveCount(3);
        results.Sum(r => r.InputTokens).Should().Be(60);
    }

    // ── GetAgentSummaryAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentSummaryAsync_SomaTotalTokensEConta()
    {
        var agentId = $"agent-sum-{Guid.NewGuid():N}";
        var from = DateTime.UtcNow.AddMinutes(-1);
        var to = DateTime.UtcNow.AddMinutes(1);

        await Repo.AppendAsync(MakeUsage(agentId, inputTokens: 100, outputTokens: 50));
        await Repo.AppendAsync(MakeUsage(agentId, inputTokens: 200, outputTokens: 100));

        var summary = await Repo.GetAgentSummaryAsync(agentId, from, to);

        summary.TotalInput.Should().Be(300);
        summary.TotalOutput.Should().Be(150);
        summary.CallCount.Should().Be(2);
    }

    // ── Filtro de data ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAgentSummaryAsync_FiltroDeData_ExcluiForaDoPeriodo()
    {
        var agentId = $"agent-dt-{Guid.NewGuid():N}";
        var pastDate = DateTime.UtcNow.AddDays(-30);

        await Repo.AppendAsync(MakeUsage(agentId, inputTokens: 999, outputTokens: 999, createdAt: pastDate));

        // Query a recent window that excludes the past record
        var from = DateTime.UtcNow.AddMinutes(-5);
        var to = DateTime.UtcNow.AddMinutes(5);
        var summary = await Repo.GetAgentSummaryAsync(agentId, from, to);

        summary.CallCount.Should().Be(0);
        summary.TotalInput.Should().Be(0);
    }

    // ── GetByAgentIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByAgentId_RetornaRegistrosDoAgente()
    {
        var agentId = $"agent-hist-{Guid.NewGuid():N}";

        await Repo.AppendAsync(MakeUsage(agentId));
        await Repo.AppendAsync(MakeUsage(agentId));

        var results = await Repo.GetByAgentIdAsync(agentId);

        results.Should().HaveCountGreaterOrEqualTo(2);
        results.Should().AllSatisfy(r => r.AgentId.Should().Be(agentId));
    }

    // ── GetAllAgentsSummaryAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetAllAgentsSummaryAsync_RetornaLista()
    {
        var from = DateTime.UtcNow.AddMinutes(-5);
        var to = DateTime.UtcNow.AddMinutes(5);

        var summaries = await Repo.GetAllAgentsSummaryAsync(from, to);

        summaries.Should().NotBeNull();
    }
}
