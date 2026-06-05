using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Services;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Services;
using EfsAiHub.Platform.Runtime.Tools.Generic.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class GenericToolServiceNormalizationTests
{
    private static (GenericToolService svc, IGenericToolRepository repo) Build()
    {
        var repo = Substitute.For<IGenericToolRepository>();
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        var propagator = Substitute.For<IAgentDependencyPropagator>();
        var projectAccessor = Substitute.For<IProjectContextAccessor>();
        projectAccessor.Current.Returns(new ProjectContext("p"));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.Current.Returns(new TenantContext("t"));
        var options = Options.Create(new GenericToolsOptions
        {
            DefaultTimeoutSeconds = 60,
            MaxTimeoutSeconds = 120,
        });

        var svc = new GenericToolService(
            repo, agentRepo, propagator, projectAccessor, tenantAccessor, options,
            new SchemaNormalizer(),
            Substitute.For<ILogger<GenericToolService>>());

        return (svc, repo);
    }

    private static GenericTool BuildPostTool(string outputSchema, string id = "") => new()
    {
        Id = id,
        ProjectId = "p",
        TenantId = "t",
        Name = "x",
        HttpMethod = HttpMethodType.POST,
        UrlTemplate = "https://api.example.com/x",
        InputContentType = InputContentType.None,
        OutputContentType = OutputContentType.Json,
        OutputSchema = outputSchema,
        OutputProjectionMode = OutputProjectionMode.Project,
    };

    [Fact]
    public async Task CreateAsync_OutputSchemaSemAdditionalProperties_PreservaSemForcar()
    {
        // Output role NÃO força strict (additionalProperties:false /
        // required:[all]) — response com campos extras passa pelo projector
        // (drop silencioso); user-declared required (vazio aqui) é preservado.
        var raw = """{"type":"object","properties":{"id":{"type":"string"}}}""";

        var (svc, repo) = Build();
        GenericTool? captured = null;
        repo.CreateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        await svc.CreateAsync(null, BuildPostTool(raw));

        captured.Should().NotBeNull();
        var canon = JsonDocument.Parse(captured!.OutputSchema!).RootElement;
        canon.TryGetProperty("additionalProperties", out _).Should().BeFalse(
            "Output canônico não força additionalProperties:false");
        canon.TryGetProperty("required", out _).Should().BeFalse(
            "Output canônico não força required quando user não declarou");
    }

    [Fact]
    public async Task CreateAsync_OutputSchemaComOneOf_PersisteSupersetEEmiteWarning()
    {
        // oneOf de 2 branches: union de properties + intersection de required.
        // Canônico não tem mais oneOf, e warning é exposto pra UI.
        var raw = """
        {
          "oneOf": [
            {"type":"object","properties":{"a":{"type":"string"}, "b":{"type":"string"}}, "required":["a"]},
            {"type":"object","properties":{"a":{"type":"string"}, "c":{"type":"string"}}, "required":["a"]}
          ]
        }
        """;

        var (svc, repo) = Build();
        GenericTool? captured = null;
        repo.CreateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var result = await svc.CreateAsync(null, BuildPostTool(raw));

        captured!.OutputSchema.Should().NotContain("oneOf");
        var canon = JsonDocument.Parse(captured.OutputSchema!).RootElement;
        var props = canon.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).ToHashSet();
        props.Should().BeEquivalentTo(new[] { "a", "b", "c" });

        result.Warnings.Should().Contain(w => w.Code == "oneof.merged");
    }

    [Fact]
    public async Task CreateAsync_OutputSchemaComRef_InlineiaAntesDePersistir()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "addr": { "$ref": "#/definitions/Address" }
          },
          "definitions": {
            "Address": {
              "type": "object",
              "properties": { "city": {"type": "string"} }
            }
          }
        }
        """;

        var (svc, repo) = Build();
        GenericTool? captured = null;
        repo.CreateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        await svc.CreateAsync(null, BuildPostTool(raw));

        captured!.OutputSchema.Should().NotContain("$ref");
        captured.OutputSchema.Should().NotContain("definitions");
        var canon = JsonDocument.Parse(captured.OutputSchema!).RootElement;
        canon.GetProperty("properties").GetProperty("addr")
             .GetProperty("properties").GetProperty("city").GetProperty("type").GetString()
             .Should().Be("string");
    }

    [Fact]
    public async Task UpdateAsync_RecanonicalizaEDevolveWarnings()
    {
        var existing = BuildPostTool(
            """{"type":"object","properties":{"x":{"type":"string"}},"required":["x"],"additionalProperties":false}""",
            id: "tool-a");

        var (svc, repo) = Build();
        repo.GetByIdAsync("tool-a", Arg.Any<CancellationToken>()).Returns(existing);
        GenericTool? captured = null;
        repo.UpdateAsync(Arg.Do<GenericTool>(t => captured = t), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<GenericTool>());

        var patch = BuildPostTool("""
        {
          "type":"object",
          "properties":{"x":{"type":"string","format":"color-hex"}},
          "required":["x"]
        }
        """);

        var result = await svc.UpdateAsync("tool-a", patch, existing.UpdatedAt);

        captured!.OutputSchema.Should().NotContain("color-hex");
        result.Warnings.Should().Contain(w => w.Code == "format.dropped");
    }
}
