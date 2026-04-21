using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentValidationTests
{
    private static AgentService BuildService()
    {
        var mcpChecker = Substitute.For<IMcpHealthChecker>();
        mcpChecker.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        return new AgentService(
            repository: Substitute.For<IAgentDefinitionRepository>(),
            promptRepo: Substitute.For<IAgentPromptRepository>(),
            mcpHealthChecker: mcpChecker,
            projectAccessor: Substitute.For<IProjectContextAccessor>(),
            logger: Substitute.For<ILogger<AgentService>>());
    }

    [Fact]
    public async Task DeploymentNameVazio_RetornaErro()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-x",
            Name = "Sem deployment",
            Model = new AgentModelConfig { DeploymentName = "" },
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("deploymentName"));
    }

    [Fact]
    public async Task DefinicaoValida_Aprovada()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-1",
            Name = "Agente Válido",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };

        var (isValid, _) = await svc.ValidateAsync(def);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task McpTool_SemServerUrl_RetornaErro()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-mcp",
            Name = "MCP Agent",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools =
            [
                new AgentToolDefinition
                {
                    Type = "mcp",
                    ServerLabel = "meu-servidor",
                    ServerUrl = null,
                    AllowedTools = ["tool1"]
                }
            ]
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("serverUrl"));
    }

    [Fact]
    public async Task McpTool_SemServerLabel_RetornaErro()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-mcp2",
            Name = "MCP Agent 2",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools =
            [
                new AgentToolDefinition
                {
                    Type = "mcp",
                    ServerLabel = null,
                    ServerUrl = "https://mcp.example.com",
                    AllowedTools = ["tool1"]
                }
            ]
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("serverLabel"));
    }

    [Fact]
    public async Task TemperatureForaDaFaixa_RetornaErro()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-t",
            Name = "Temperatura inválida",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 3.5f },
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("temperature"));
    }

    [Fact]
    public async Task IdVazio_RetornaErro()
    {
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "",
            Name = "OK",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("id"));
    }
}
