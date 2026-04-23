using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Persistence;

/// <summary>
/// F5.5 — valida o isolamento multi-project via HasQueryFilter novo em
/// LlmTokenUsageRow (F4). Cria 2 scopes DI com ProjectContext distinto,
/// escreve rows em cada e confirma que leituras cross-project retornam
/// vazio.
/// </summary>
[Collection("Integration")]
[Trait("Category", "Integration")]
public class LlmTokenUsageIsolationTests(IntegrationWebApplicationFactory factory)
{
    private static LlmTokenUsage MakeUsage(string executionId, string projectId) => new()
    {
        AgentId = $"agent-isolation-{Guid.NewGuid():N}",
        ModelId = "gpt-4o",
        ExecutionId = executionId,
        InputTokens = 10,
        OutputTokens = 5,
        TotalTokens = 15,
        DurationMs = 100,
        ProjectId = projectId,
    };

    /// <summary>
    /// Cria um novo scope DI com ProjectContext explícito. Permite exercitar
    /// HasQueryFilter que usa IProjectContextAccessor.Current.
    /// </summary>
    private static IServiceScope ScopeWithProject(
        IntegrationWebApplicationFactory factory, string projectId)
    {
        var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IProjectContextAccessor>();
        accessor.Current = new ProjectContext(projectId);
        return scope;
    }

    [Fact]
    public async Task Read_CrossProject_NaoVazaRowsDeOutroProject()
    {
        var execA = $"exec-A-{Guid.NewGuid():N}";
        var execB = $"exec-B-{Guid.NewGuid():N}";

        // Escreve no scope do projA
        using (var scopeA = ScopeWithProject(factory, "projA"))
        {
            var repoA = scopeA.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();
            await repoA.AppendAsync(MakeUsage(execA, projectId: "projA"));
            await repoA.AppendAsync(MakeUsage(execB, projectId: "projB")); // escrita "com scope errado"
        }

        // Lê no scope do projA — só deve retornar rows com ProjectId=projA
        // (ou null, que é tolerante por F4 — mas F5.5 backfill limpou).
        using (var scopeA = ScopeWithProject(factory, "projA"))
        {
            var repoA = scopeA.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();
            var rowsA = await repoA.GetByExecutionIdAsync(execA);
            var rowsBfromA = await repoA.GetByExecutionIdAsync(execB);

            rowsA.Should().ContainSingle("projA enxerga a row que escreveu");
            rowsBfromA.Should().BeEmpty(
                "row escrita com ProjectId=projB NÃO deve aparecer quando CurrentProject=projA");
        }

        // Lê no scope do projB — vê a row de execB (que foi escrita com projB).
        using (var scopeB = ScopeWithProject(factory, "projB"))
        {
            var repoB = scopeB.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();
            var rowsB = await repoB.GetByExecutionIdAsync(execB);
            var rowsAfromB = await repoB.GetByExecutionIdAsync(execA);

            rowsB.Should().ContainSingle("projB enxerga a row escrita com seu ProjectId");
            rowsAfromB.Should().BeEmpty(
                "row escrita com ProjectId=projA NÃO deve aparecer quando CurrentProject=projB");
        }
    }

    [Fact]
    public async Task Read_SameProject_EnxergaAPropriaRow()
    {
        var executionId = $"exec-iso-{Guid.NewGuid():N}";

        using (var scope = ScopeWithProject(factory, "projX"))
        {
            var repo = scope.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();
            await repo.AppendAsync(MakeUsage(executionId, projectId: "projX"));
        }

        using (var scope = ScopeWithProject(factory, "projX"))
        {
            var repo = scope.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();
            var rows = await repo.GetByExecutionIdAsync(executionId);

            rows.Should().ContainSingle();
            rows[0].ProjectId.Should().Be("projX");
        }
    }
}
