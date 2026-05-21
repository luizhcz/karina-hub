using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericToolInvariantsTests
{
    [Fact]
    public void EnsureInvariants_ToolValido_NaoLanca()
    {
        var tool = ValidGetTool();

        var act = tool.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_NameVazio_LancaDomainException()
    {
        var tool = ValidGetTool();
        tool.Name = "";

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*Name*");
    }

    [Theory]
    [InlineData("Buscar Cliente")] // espaço
    [InlineData("café")]            // acento
    [InlineData("rocket🚀")]        // emoji
    [InlineData("1get")]            // começa com dígito
    [InlineData("-leading")]        // começa com hífen
    [InlineData("name.with.dot")]   // ponto
    public void EnsureInvariants_NameForaDoPattern_LancaDomainException(string invalidName)
    {
        var tool = ValidGetTool();
        tool.Name = invalidName;

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*inválido*");
    }

    [Theory]
    [InlineData("get_quote")]
    [InlineData("lookup-user")]
    [InlineData("_internal")]
    [InlineData("a")]               // 1 char minimo
    [InlineData("x123-456_789")]
    public void EnsureInvariants_NameValido_NaoLanca(string validName)
    {
        var tool = ValidGetTool();
        tool.Name = validName;

        Action act = tool.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_UrlSemPlaceholderMasComPathParam_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.example.com/users",
            PathParams = new Dictionary<string, ParamDefinition>
            {
                ["id"] = new("string", "user id", true),
            },
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*PathParams*ausentes no UrlTemplate*");
    }

    [Fact]
    public void EnsureInvariants_PlaceholderSemPathParam_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.example.com/users/{id}",
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*placeholders sem PathParam*");
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("content-type")]
    [InlineData("Accept")]
    [InlineData("ACCEPT")]
    public void EnsureInvariants_HeaderReservado_LancaDomainException(string header)
    {
        var tool = ValidGetTool();
        tool.CustomHeaders = new Dictionary<string, string> { [header] = "*/*" };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*header reservado*");
    }

    [Fact]
    public void EnsureInvariants_GetComInputContentTypeNonNone_LancaDomainException()
    {
        var tool = ValidGetTool();
        tool.InputContentType = InputContentType.Json;
        tool.InputSchema = "{\"type\":\"object\",\"properties\":{}}";

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*GET*InputContentType=None*");
    }

    [Fact]
    public void EnsureInvariants_FormUrlEncodedComSchemaAninhado_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.example.com/post",
            InputContentType = InputContentType.FormUrlEncoded,
            InputSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "address": { "type": "object", "properties": { "street": { "type": "string" } } }
                    }
                }
                """,
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*FormUrlEncoded*plano*address*");
    }

    [Fact]
    public void EnsureInvariants_FormUrlEncodedComSchemaPlano_NaoLanca()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.example.com/post",
            InputContentType = InputContentType.FormUrlEncoded,
            InputSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "age": { "type": "integer" }
                    }
                }
                """,
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var act = tool.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_JsonInputSemSchema_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.example.com/post",
            InputContentType = InputContentType.Json,
            InputSchema = null,
            OutputContentType = OutputContentType.Text,
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*InputSchema*");
    }

    [Fact]
    public void EnsureInvariants_TextOutput_NaoExigeOutputSchema()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.example.com/get",
            OutputContentType = OutputContentType.Text,
            OutputSchema = null,
        };

        var act = tool.EnsureInvariants;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_TimeoutOverrideZero_LancaDomainException()
    {
        var tool = ValidGetTool();
        tool.TimeoutSecondsOverride = 0;

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*TimeoutSecondsOverride*");
    }

    [Fact]
    public void EnsureWithinTimeoutCeiling_OverrideMaiorQueMax_LancaDomainException()
    {
        var tool = ValidGetTool();
        tool.TimeoutSecondsOverride = 200;

        Action act = () => tool.EnsureWithinTimeoutCeiling(120);

        act.Should().Throw<DomainException>().WithMessage("*excede o limite global*");
    }

    [Fact]
    public void EnsureWithinTimeoutCeiling_OverrideNull_NaoLanca()
    {
        var tool = ValidGetTool();
        tool.TimeoutSecondsOverride = null;

        Action act = () => tool.EnsureWithinTimeoutCeiling(120);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInvariants_CsvOutputSemSchema_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.example.com/data.csv",
            OutputContentType = OutputContentType.Csv,
            OutputSchema = null,
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*OutputSchema*");
    }

    [Fact]
    public void EnsureInvariants_FormUrlEncodedComJsonInvalido_LancaDomainException()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.example.com/post",
            InputContentType = InputContentType.FormUrlEncoded,
            InputSchema = "{ not valid json",
            OutputContentType = OutputContentType.Text,
        };

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>()
            .WithMessage("*InputSchema*JSON válido*");
    }

    [Fact]
    public void EnsureInvariants_IdVazio_LancaDomainException()
    {
        var tool = ValidGetTool();
        var bad = new GenericTool
        {
            Id = "",
            ProjectId = tool.ProjectId,
            TenantId = tool.TenantId,
            Name = tool.Name,
            HttpMethod = tool.HttpMethod,
            UrlTemplate = tool.UrlTemplate,
            PathParams = tool.PathParams,
            OutputContentType = tool.OutputContentType,
            OutputSchema = tool.OutputSchema,
        };

        Action act = bad.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*Id*");
    }

    [Fact]
    public void EnsureInvariants_UrlTemplateVazio_LancaDomainException()
    {
        var tool = ValidGetTool();
        tool.UrlTemplate = "";

        Action act = tool.EnsureInvariants;

        act.Should().Throw<DomainException>().WithMessage("*UrlTemplate*");
    }

    [Fact]
    public void ExtractPlaceholders_MultiplosUnicos_RetornaSet()
    {
        var url = "https://api.example.com/{tenant}/users/{id}/items/{id}";

        var placeholders = GenericTool.ExtractPlaceholders(url);

        placeholders.Should().BeEquivalentTo(new[] { "tenant", "id" });
    }

    private static GenericTool ValidGetTool() => new()
    {
        Id = "search-users",
        ProjectId = "alpha",
        TenantId = "tenant-a",
        Name = "search-users",
        HttpMethod = HttpMethodType.GET,
        UrlTemplate = "https://api.example.com/users/{id}",
        PathParams = new Dictionary<string, ParamDefinition>
        {
            ["id"] = new("string", "user id", true),
        },
        OutputContentType = OutputContentType.Json,
        OutputSchema = "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}",
    };
}
