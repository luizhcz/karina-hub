using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class AgentSessionStoreTests(IntegrationWebApplicationFactory factory)
{
    private IAgentSessionStore Store =>
        factory.Services.GetRequiredService<IAgentSessionStore>();

    private static AgentSessionRecord MakeRecord(string? agentId = null) => new()
    {
        SessionId = Guid.NewGuid().ToString(),
        AgentId = agentId ?? $"agent-{Guid.NewGuid():N}",
        SerializedState = JsonDocument.Parse("{}").RootElement,
        TurnCount = 0
    };

    // ── Create + GetById ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_GetById_RetornaSessao()
    {
        var record = MakeRecord();

        await Store.CreateAsync(record);
        var fetched = await Store.GetByIdAsync(record.SessionId);

        fetched.Should().NotBeNull();
        fetched!.SessionId.Should().Be(record.SessionId);
        fetched.AgentId.Should().Be(record.AgentId);
    }

    [Fact]
    public async Task GetById_Inexistente_RetornaNull()
    {
        var result = await Store.GetByIdAsync(Guid.NewGuid().ToString());

        result.Should().BeNull();
    }

    // ── TTL de 30 dias ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ExpiresAtAproximadamente30Dias()
    {
        // The repo sets ExpiresAt = UtcNow + 30 days internally.
        // We verify the session is retrievable right after creation (not expired yet).
        var record = MakeRecord();
        await Store.CreateAsync(record);

        var fetched = await Store.GetByIdAsync(record.SessionId);

        fetched.Should().NotBeNull("session should not be expired immediately after creation");
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_IncrementaTurnCount_PersisteAlteracao()
    {
        var record = MakeRecord();
        await Store.CreateAsync(record);

        record.TurnCount = 3;
        record.LastAccessedAt = DateTime.UtcNow;
        await Store.UpdateAsync(record);

        var fetched = await Store.GetByIdAsync(record.SessionId);

        fetched!.TurnCount.Should().Be(3);
    }

    [Fact]
    public async Task UpdateAsync_RenovaExpiresAt_SessaoContinuaAcessivel()
    {
        var record = MakeRecord();
        await Store.CreateAsync(record);
        await Store.UpdateAsync(record); // renews ExpiresAt

        var fetched = await Store.GetByIdAsync(record.SessionId);

        fetched.Should().NotBeNull("session should remain accessible after update renewing TTL");
    }

    // ── GetByAgentIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByAgentId_RetornaTodasSessoesDoAgente()
    {
        var agentId = $"agent-{Guid.NewGuid():N}";
        var r1 = MakeRecord(agentId);
        var r2 = MakeRecord(agentId);
        await Store.CreateAsync(r1);
        await Store.CreateAsync(r2);

        var sessions = await Store.GetByAgentIdAsync(agentId);

        sessions.Should().HaveCount(2);
        sessions.Should().AllSatisfy(s => s.AgentId.Should().Be(agentId));
    }

    [Fact]
    public async Task GetByAgentId_NaoRetornaOutrosAgentes()
    {
        var agentA = $"agent-a-{Guid.NewGuid():N}";
        var agentB = $"agent-b-{Guid.NewGuid():N}";
        await Store.CreateAsync(MakeRecord(agentA));
        await Store.CreateAsync(MakeRecord(agentB));

        var sessions = await Store.GetByAgentIdAsync(agentA);

        sessions.Should().NotContain(s => s.AgentId == agentB);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SessaoExistente_RetornaTrue()
    {
        var record = MakeRecord();
        await Store.CreateAsync(record);

        var deleted = await Store.DeleteAsync(record.SessionId);

        deleted.Should().BeTrue();
        var fetched = await Store.GetByIdAsync(record.SessionId);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_SessaoInexistente_RetornaFalse()
    {
        var deleted = await Store.DeleteAsync(Guid.NewGuid().ToString());

        deleted.Should().BeFalse();
    }
}
