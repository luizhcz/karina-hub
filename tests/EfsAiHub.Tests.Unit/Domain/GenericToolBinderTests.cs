using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Interfaces;
using EfsAiHub.Platform.Runtime.Tools.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericToolBinderTests
{
    private static (GenericToolBinder binder,
        IGenericToolRepository repo,
        IGenericToolExecutor executor,
        IFunctionToolRegistry registry) Build()
    {
        var repo = Substitute.For<IGenericToolRepository>();
        var executor = Substitute.For<IGenericToolExecutor>();
        var registry = Substitute.For<IFunctionToolRegistry>();
        var binder = new GenericToolBinder(repo, executor, registry, NullLogger<GenericToolBinder>.Instance);
        return (binder, repo, executor, registry);
    }

    private static GenericTool ValidTool(string id) => new()
    {
        Id = id,
        ProjectId = "p",
        TenantId = "t",
        Name = id,
        HttpMethod = HttpMethodType.GET,
        UrlTemplate = "https://api.example.com/data",
        OutputContentType = OutputContentType.Json,
        OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
    };

    private static AgentDefinition AgentWith(params AgentToolDefinition[] tools) => new()
    {
        Id = "agent-x",
        Name = "X",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        ProjectId = "p",
        TenantId = "t",
        Tools = tools,
    };

    [Fact]
    public async Task BindAsync_AgentSemTools_NaoChamaRegistry()
    {
        var (binder, _, _, registry) = Build();
        var agent = AgentWith();

        await binder.BindAsync(agent);

        registry.DidNotReceiveWithAnyArgs().Register(default!, default!, default!);
    }

    [Fact]
    public async Task BindAsync_AgentSemGenericHttp_NaoChamaRepo()
    {
        var (binder, repo, _, registry) = Build();
        var agent = AgentWith(new AgentToolDefinition { Type = "function", Name = "search" });

        await binder.BindAsync(agent);

        await repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default!);
        registry.DidNotReceiveWithAnyArgs().Register(default!, default!, default!);
    }

    [Fact]
    public async Task BindAsync_GenericToolEncontrado_RegistraNoRegistry()
    {
        var (binder, repo, _, registry) = Build();
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(ValidTool("tool-a"));

        var agent = AgentWith(new AgentToolDefinition
        {
            Type = "generic_http",
            GenericToolId = "tool-a",
        });

        await binder.BindAsync(agent);

        registry.Received(1).Register(
            "tool-a",
            Arg.Any<AIFunction>(),
            "p");
    }

    [Fact]
    public async Task BindAsync_GenericToolNaoEncontrado_LogaWarningESkipa()
    {
        var (binder, repo, _, registry) = Build();
        repo.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((GenericTool?)null);

        var agent = AgentWith(new AgentToolDefinition
        {
            Type = "generic_http",
            GenericToolId = "missing",
        });

        await binder.BindAsync(agent);

        registry.DidNotReceiveWithAnyArgs().Register(default!, default!, default!);
    }

    [Fact]
    public async Task BindAsync_MisturaTipos_RegistraSomenteGeneric()
    {
        var (binder, repo, _, registry) = Build();
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(ValidTool("tool-a"));

        var agent = AgentWith(
            new AgentToolDefinition { Type = "function", Name = "search" },
            new AgentToolDefinition { Type = "generic_http", GenericToolId = "tool-a" },
            new AgentToolDefinition { Type = "mcp", McpServerId = "mcp-1" });

        await binder.BindAsync(agent);

        registry.Received(1).Register("tool-a", Arg.Any<AIFunction>(), "p");
    }

    [Fact]
    public async Task BindAsync_GenericHttpSemGenericToolId_Skipa()
    {
        var (binder, repo, _, registry) = Build();

        var agent = AgentWith(new AgentToolDefinition
        {
            Type = "generic_http",
            GenericToolId = null,
        });

        await binder.BindAsync(agent);

        await repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default!);
        registry.DidNotReceiveWithAnyArgs().Register(default!, default!, default!);
    }
}
