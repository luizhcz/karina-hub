using System.Net;
using System.Text;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class HttpPersonaProviderTests
{
    // Handler in-memory — intercepta qualquer request e responde conforme script.
    // Cada chamada invoca _respond(request); teste controla status + body.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int CallCount { get; private set; }
        public List<string> Paths { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            Paths.Add(request.RequestUri?.AbsolutePath ?? "");
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false);
    }

    private static HttpPersonaProvider Build(StubHandler handler, PersonaApiOptions? opts = null)
    {
        var options = Options.Create(opts ?? new PersonaApiOptions
        {
            BaseUrl = "https://fake-persona-api.test/",
            ApiKey = "k",
            TimeoutSeconds = 3,
        });
        return new HttpPersonaProvider(
            new SingleClientFactory(handler),
            options,
            NullLogger<HttpPersonaProvider>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    // ── Cliente ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Cliente_Ok_DesserializaCamelCase()
    {
        // Payload real da API externa deve ter todos os campos em camelCase.
        var body = """
        {
          "clientName": "João Silva",
          "suitabilityLevel": "moderado",
          "suitabilityDescription": "Perfil moderado",
          "businessSegment": "private",
          "country": "BR",
          "isOffshore": true
        }
        """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var provider = Build(handler);

        var persona = await provider.ResolveAsync("u1", "cliente");

        persona.Should().BeOfType<ClientPersona>();
        var c = (ClientPersona)persona;
        c.ClientName.Should().Be("João Silva");
        c.SuitabilityLevel.Should().Be("moderado");
        c.BusinessSegment.Should().Be("private");
        c.Country.Should().Be("BR");
        c.IsOffshore.Should().BeTrue();
        handler.Paths.Single().Should().EndWith("/personas/clientes/u1");
    }

    [Fact]
    public async Task Resolve_Cliente_404_RetornaAnonymous()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var persona = await Build(handler).ResolveAsync("u1", "cliente");

        persona.Should().BeOfType<ClientPersona>();
        persona.IsAnonymous.Should().BeTrue();
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Admin_Ok_DesserializaListasEBooleanCamelCase()
    {
        // Nota: API externa envia "isWM" (WM maiúsculo) — JsonPropertyName no
        // DTO mapeia pra IsWm (C# PascalCase correto). Teste cobre esse mapping
        // que teria pego o B2 (frontend/backend divergentes).
        var body = """
        {
          "username": "ana.gestora",
          "partnerType": "GESTOR",
          "segments": ["B2B", "WM", "IB"],
          "institutions": ["BTG", "EQI"],
          "isInternal": true,
          "isWM": true,
          "isMaster": false,
          "isBroker": true
        }
        """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        var provider = Build(handler);

        var persona = await provider.ResolveAsync("admin-1", "admin");

        persona.Should().BeOfType<AdminPersona>();
        var a = (AdminPersona)persona;
        a.Username.Should().Be("ana.gestora");
        a.PartnerType.Should().Be("GESTOR");
        a.Segments.Should().BeEquivalentTo(new[] { "B2B", "WM", "IB" });
        a.Institutions.Should().BeEquivalentTo(new[] { "BTG", "EQI" });
        a.IsInternal.Should().BeTrue();
        a.IsWm.Should().BeTrue();
        a.IsMaster.Should().BeFalse();
        a.IsBroker.Should().BeTrue();
        handler.Paths.Single().Should().EndWith("/personas/admins/admin-1");
    }

    [Fact]
    public async Task Resolve_Admin_ListasNulas_NormalizadasParaVazias()
    {
        // API pode vir sem os arrays — provider normaliza pra empty (contrato:
        // IReadOnlyList nunca null no record).
        var body = """{"username":"x","partnerType":"DEFAULT"}""";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));

        var persona = (AdminPersona)await Build(handler).ResolveAsync("u1", "admin");

        persona.Segments.Should().BeEmpty();
        persona.Institutions.Should().BeEmpty();
    }

    // ── Falhas ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_PayloadInvalido_DaiFallbackAnonymousSemRetry()
    {
        // JsonException não entra no whitelist de retry — payload corrompido
        // tende a se repetir, retry é desperdício. Fallback direto + warning.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{not valid json"));

        var persona = await Build(handler).ResolveAsync("u1", "cliente");

        persona.IsAnonymous.Should().BeTrue();
        handler.CallCount.Should().Be(1); // sem retry
    }

    [Fact]
    public async Task Resolve_5xx_Retenta_EFallback()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var persona = await Build(handler).ResolveAsync("u1", "cliente");

        persona.IsAnonymous.Should().BeTrue();
        handler.CallCount.Should().Be(2); // retry
    }

    [Fact]
    public async Task Resolve_Disabled_NaoChamaHttpERetornaAnonymous()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("não deveria ser chamado"));
        var provider = Build(handler, new PersonaApiOptions { Disabled = true, BaseUrl = "x" });

        var persona = await provider.ResolveAsync("u1", "cliente");

        persona.IsAnonymous.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_UserTypeDesconhecido_RetornaAnonymousSemChamarApi()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("não deveria ser chamado"));

        var persona = await Build(handler).ResolveAsync("u1", "desconhecido");

        persona.IsAnonymous.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_ExceptionNaoRecoverable_Propaga()
    {
        // Contrato do C5: só falhas de transport/schema viram Anonymous.
        // InvalidOperationException (bug lógico) DEVE subir pro middleware
        // global — senão bug fica silenciado como "API instável".
        var handler = new StubHandler(_ => throw new InvalidOperationException("bug lógico"));

        var act = async () => await Build(handler).ResolveAsync("u1", "cliente");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("bug lógico");
    }

    [Fact]
    public async Task Resolve_Cancelamento_RetornaAnonymousSemRetry()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var persona = await Build(handler).ResolveAsync("u1", "cliente", cts.Token);

        persona.IsAnonymous.Should().BeTrue();
    }
}
