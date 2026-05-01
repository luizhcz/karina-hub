using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AgentValidationTests
{
    private static AgentService BuildService()
    {
        return new AgentService(
            repository: Substitute.For<IAgentDefinitionRepository>(),
            promptRepo: Substitute.For<IAgentPromptRepository>(),
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
    public async Task McpTool_SemIdNemInline_RetornaErro()
    {
        // Novo contrato: MCP tool exige McpServerId OU (ServerLabel + ServerUrl inline).
        // Sem nenhum dos dois → erro.
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-mcp",
            Name = "MCP Agent",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools = [new AgentToolDefinition { Type = "mcp" }]
        };

        var (isValid, errors) = await svc.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("mcpServerId") || e.Contains("serverLabel"));
    }

    [Fact]
    public async Task McpTool_ComMcpServerId_Aprovada()
    {
        // Id-based: basta o McpServerId. ServerLabel/ServerUrl/AllowedTools são resolvidos
        // em runtime pelo provider a partir do registry aihub.mcp_servers.
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-mcp-id",
            Name = "MCP Agent ID-based",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools = [new AgentToolDefinition { Type = "mcp", McpServerId = "mcp-server-123" }]
        };

        var (isValid, _) = await svc.ValidateAsync(def);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task McpTool_InlineSemUrl_RetornaErro()
    {
        // Legacy inline: se McpServerId é null mas ServerLabel está presente,
        // exige ServerUrl válida.
        var svc = BuildService();
        var def = new AgentDefinition
        {
            Id = "agent-mcp-inline",
            Name = "MCP Agent Inline",
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
        errors.Should().Contain(e => e.Contains("mcpServerId") || e.Contains("serverUrl"));
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

    // ── Visibility ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("project")]
    [InlineData("global")]
    [InlineData("PROJECT")]
    [InlineData("Global")]
    public void EnsureInvariants_VisibilityValida_Passa(string visibility)
    {
        var def = new AgentDefinition
        {
            Id = "agent-vis",
            Name = "Visibility test",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Visibility = visibility,
        };

        var act = () => def.EnsureInvariants();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("")]
    [InlineData("garbage")]
    public void EnsureInvariants_VisibilityInvalida_Throws(string visibility)
    {
        var def = new AgentDefinition
        {
            Id = "agent-vis-bad",
            Name = "Visibility inválida",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Visibility = visibility,
        };

        var act = () => def.EnsureInvariants();
        act.Should().Throw<EfsAiHub.Core.Abstractions.Exceptions.DomainException>()
            .WithMessage("*Visibility*");
    }
}
