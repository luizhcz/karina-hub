using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class NotificationsEndpointsTests(IntegrationWebApplicationFactory factory)
{
    private IAgentService AgentService => factory.Services.GetRequiredService<IAgentService>();
    private IAgentVersionRepository VersionRepo => factory.Services.GetRequiredService<IAgentVersionRepository>();

    /// <summary>
    /// Cria agent + publica version explícita com flag breaking. Retorna o agentId
    /// pra assertion. Service-level (bypass do WorkflowsController DI chain que tem
    /// pre-existing AWS Secrets Manager issue na fixture).
    /// </summary>
    private async Task<string> SetupBreakingVersionAsync(string changeReason)
    {
        var agentId = $"agent-bc-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Breaking Test",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v1"));

        var defV2 = AgentDefinition.Create(
            id: agentId,
            name: "Agent Breaking Test",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v2 — schema mudou");
        await AgentService.UpdateAsync(defV2,
            breakingChange: true,
            changeReason: changeReason,
            createdBy: "user-test");

        return agentId;
    }

    [Fact]
    public async Task ListRecentBreaking_RetornaVersionsPublicadasComBreakingTrue()
    {
        var agentId = await SetupBreakingVersionAsync("schema mudou pra notification test");

        var versions = await VersionRepo.ListRecentBreakingAsync(sinceDays: 7);

        versions.Should().NotBeEmpty();
        versions.Should().Contain(v =>
            v.AgentDefinitionId == agentId &&
            v.BreakingChange == true &&
            v.ChangeReason == "schema mudou pra notification test");
    }

    [Fact]
    public async Task ListRecentBreaking_VersionsNaoBreaking_NaoSaoIncluidas()
    {
        var agentId = $"agent-patch-{Guid.NewGuid():N}";
        await AgentService.CreateAsync(AgentDefinition.Create(
            id: agentId,
            name: "Agent Patch Only",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v1"));

        // Update sem breakingChange (auto-snapshot null) e com breakingChange=false (patch).
        var defV2 = AgentDefinition.Create(
            id: agentId,
            name: "Agent Patch Only",
            model: new AgentModelConfig { DeploymentName = "gpt-4o" },
            instructions: "v2 patch");
        await AgentService.UpdateAsync(defV2, breakingChange: false, changeReason: "fix typo");

        var versions = await VersionRepo.ListRecentBreakingAsync(sinceDays: 7);

        versions.Should().NotContain(v => v.AgentDefinitionId == agentId);
    }

    [Fact]
    public async Task ListRecentBreaking_DaysZero_RetornaVazio()
    {
        await SetupBreakingVersionAsync("anything");

        var versions = await VersionRepo.ListRecentBreakingAsync(sinceDays: 0);

        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRecentBreaking_OrdenaDescendentePorCreatedAt()
    {
        await SetupBreakingVersionAsync("primeiro");
        await Task.Delay(20); // garante CreatedAt distinto.
        await SetupBreakingVersionAsync("segundo");

        var versions = await VersionRepo.ListRecentBreakingAsync(sinceDays: 7);

        versions.Should().HaveCountGreaterThan(1);
        // Mais recente vem primeiro.
        var sortedDesc = versions.OrderByDescending(v => v.CreatedAt).ToList();
        versions.Should().Equal(sortedDesc);
    }
}
