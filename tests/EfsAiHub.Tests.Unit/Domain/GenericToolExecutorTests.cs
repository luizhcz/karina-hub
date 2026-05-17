using System.Net;
using System.Text;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Platform.Runtime.Configuration;
using EfsAiHub.Platform.Runtime.Tools.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericToolExecutorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((req, _) => Task.FromResult(respond(req)))
        { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(ct));
            else
                Bodies.Add(string.Empty);
            return await _respond(request, ct);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static GenericToolExecutor BuildExecutor(StubHandler handler, GenericToolsOptions? opts = null)
    {
        return new GenericToolExecutor(
            new SingleClientFactory(handler),
            Options.Create(opts ?? new GenericToolsOptions { DefaultTimeoutSeconds = 5, MaxTimeoutSeconds = 10 }),
            Substitute.For<IRequestAuthContextAccessor>(),
            NullLogger<GenericToolExecutor>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Plain(HttpStatusCode status, string body, string mediaType = "text/plain") =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };

    [Fact]
    public async Task ExecuteAsync_GetComJsonOutput_RetornaSuccessComJsonElement()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{\"id\":42,\"name\":\"foo\"}"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/data",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Data.Should().BeOfType<System.Text.Json.JsonElement>();
        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().Headers.Accept.ToString().Should().Contain("application/json");
    }

    [Fact]
    public async Task ExecuteAsync_GetComPathParam_SubstituiPlaceholderEEscapaValue()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/users/{id}",
            PathParams = new Dictionary<string, ParamDefinition>
            {
                ["id"] = new("string", "user id", true),
            },
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>
        {
            ["id"] = "abc 123",
        });

        result.Success.Should().BeTrue();
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Be("https://api.test/users/abc%20123");
    }

    [Fact]
    public async Task ExecuteAsync_GetComQueryParams_AnexaQuerystring()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/search",
            QueryParams = new Dictionary<string, ParamDefinition>
            {
                ["q"] = new("string", "term", true),
                ["limit"] = new("integer", "limit", false),
            },
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>
        {
            ["q"] = "foo bar",
            ["limit"] = 10,
        });

        result.Success.Should().BeTrue();
        var url = handler.Requests.Single().RequestUri!.AbsoluteUri;
        url.Should().Contain("q=foo%20bar");
        url.Should().Contain("limit=10");
    }

    [Fact]
    public async Task ExecuteAsync_PostJson_SerializaBodyComContentTypeJson()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.test/post",
            InputContentType = InputContentType.Json,
            InputSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>
        {
            ["name"] = "alice",
        });

        result.Success.Should().BeTrue();
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        handler.Bodies.Single().Should().Contain("\"name\":\"alice\"");
    }

    [Fact]
    public async Task ExecuteAsync_PostText_EnviaCampoStringComoBodyCru()
    {
        var handler = new StubHandler(_ => Plain(HttpStatusCode.OK, "ok"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.test/post",
            InputContentType = InputContentType.Text,
            InputSchema = "{\"type\":\"object\",\"properties\":{\"raw\":{\"type\":\"string\"}}}",
            OutputContentType = OutputContentType.Text,
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>
        {
            ["raw"] = "Hello, World!",
        });

        result.Success.Should().BeTrue();
        var req = handler.Requests.Single();
        req.Content!.Headers.ContentType!.MediaType.Should().Be("text/plain");
        handler.Bodies.Single().Should().Be("Hello, World!");
    }

    [Fact]
    public async Task ExecuteAsync_PostFormUrlEncoded_SerializaPaisFormat()
    {
        var handler = new StubHandler(_ => Plain(HttpStatusCode.OK, "ok"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api.test/post",
            InputContentType = InputContentType.FormUrlEncoded,
            InputSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"age\":{\"type\":\"integer\"}}}",
            OutputContentType = OutputContentType.Text,
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>
        {
            ["name"] = "ana",
            ["age"] = 30,
        });

        result.Success.Should().BeTrue();
        var req = handler.Requests.Single();
        req.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
        handler.Bodies.Single().Should().Contain("name=ana");
        handler.Bodies.Single().Should().Contain("age=30");
    }

    [Fact]
    public async Task ExecuteAsync_CsvOutput_RetornaListaDeDicionarios()
    {
        var csv = "id,name\n1,foo\n2,bar\n";
        var handler = new StubHandler(_ => Plain(HttpStatusCode.OK, csv, "text/csv"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/data.csv",
            OutputContentType = OutputContentType.Csv,
            OutputSchema = "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeTrue();
        var rows = result.Data as List<Dictionary<string, string>>;
        rows.Should().NotBeNull();
        rows!.Should().HaveCount(2);
        rows![0].Should().Contain(new KeyValuePair<string, string>("id", "1"));
        rows[0].Should().Contain(new KeyValuePair<string, string>("name", "foo"));
        rows[1]["id"].Should().Be("2");
    }

    [Fact]
    public async Task ExecuteAsync_Status500_RetornaSuccessFalseSemThrow()
    {
        var handler = new StubHandler(_ => Plain(HttpStatusCode.InternalServerError, "boom"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/data",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Não foi possível executar o request do tool");
    }

    [Fact]
    public async Task ExecuteAsync_NetworkException_RetornaSuccessFalse()
    {
        var handler = new StubHandler((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("connection refused")));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/data",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Não foi possível executar o request do tool");
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_RetornaSuccessFalse()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Plain(HttpStatusCode.OK, "should not arrive");
        });
        var executor = BuildExecutor(handler, new GenericToolsOptions { DefaultTimeoutSeconds = 1, MaxTimeoutSeconds = 1 });

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/slow",
            OutputContentType = OutputContentType.Text,
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Não foi possível executar o request do tool");
    }

    [Fact]
    public async Task ExecuteAsync_JsonParseError_RetornaSuccessFalse()
    {
        var handler = new StubHandler(_ => Plain(HttpStatusCode.OK, "{ not valid json", "application/json"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/bad",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_HeadersCustomizados_AplicadosNoRequest()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var executor = BuildExecutor(handler);

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/data",
            CustomHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer abc",
                ["X-Tenant"] = "tnt",
            },
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        await executor.ExecuteAsync(tool, new Dictionary<string, object?>());

        var req = handler.Requests.Single();
        req.Headers.Authorization!.ToString().Should().Be("Bearer abc");
        req.Headers.GetValues("X-Tenant").Should().ContainSingle().Which.Should().Be("tnt");
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_PropagaOperationCanceledException()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Plain(HttpStatusCode.OK, "");
        });
        var executor = BuildExecutor(handler, new GenericToolsOptions { DefaultTimeoutSeconds = 30, MaxTimeoutSeconds = 30 });

        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "tnt",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api.test/slow",
            OutputContentType = OutputContentType.Text,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Func<Task> act = () => executor.ExecuteAsync(tool, new Dictionary<string, object?>(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
