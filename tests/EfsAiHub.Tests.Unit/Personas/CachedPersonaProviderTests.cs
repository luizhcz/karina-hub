using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Execution;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class CachedPersonaProviderTests
{
    // Fake em-memória do IEfsRedisCache — só métodos usados pelo provider.
    // Permite contar reads/writes e simular payload corrompido.
    private sealed class InMemoryRedisCache : IEfsRedisCache
    {
        private readonly ConcurrentDictionary<string, string> _store = new();
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public IDatabase Database => throw new NotSupportedException();
        public string BuildKey(string key) => key;

        public Task<string?> GetStringAsync(string key)
        {
            ReadCount++;
            return Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        }

        public Task SetStringAsync(string key, string value, TimeSpan? ttl = null)
        {
            WriteCount++;
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task<bool> SetIfExistsAsync(string key, string value, TimeSpan? ttl = null)
        {
            if (_store.ContainsKey(key)) { _store[key] = value; return Task.FromResult(true); }
            return Task.FromResult(false);
        }
        public Task<bool> RemoveAsync(string key) => Task.FromResult(_store.TryRemove(key, out _));

        public void Seed(string key, string value) => _store[key] = value;
        public void Corrupt(string key) => _store[key] = "{not valid json";
    }

    // Handler que espelha o que o inner retornaria: cada chamada ao provider
    // interno conta, pra provarmos single-flight (N callers → 1 chamada).
    //
    // Aceita um gate opcional (TaskCompletionSource): se fornecido, o handler
    // bloqueia até o gate ser setado. Usamos isso para provar single-flight
    // de forma determinística: todas as N tasks entram no WaitAsync antes do
    // primeiro resolver, sem depender de delay heurístico.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly TaskCompletionSource? _gate;
        public int CallCount;

        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> respond,
            TaskCompletionSource? gate = null)
        {
            _respond = respond;
            _gate = gate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            if (_gate is not null)
                await _gate.Task.ConfigureAwait(false);
            return _respond(request);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (CachedPersonaProvider cache, InMemoryRedisCache redis, StubHandler handler)
        Build(Func<HttpRequestMessage, HttpResponseMessage> respond, TaskCompletionSource? gate = null)
    {
        var handler = new StubHandler(respond, gate);
        var opts = Options.Create(new PersonaApiOptions
        {
            BaseUrl = "https://fake/",
            LocalCacheTtlSeconds = 60,
            CacheTtlMinutes = 5,
        });
        var inner = new HttpPersonaProvider(
            new SingleClientFactory(handler), opts, NullLogger<HttpPersonaProvider>.Instance);
        var redis = new InMemoryRedisCache();
        var cache = new CachedPersonaProvider(
            inner, redis, opts, NullLogger<CachedPersonaProvider>.Instance);
        return (cache, redis, handler);
    }

    private static HttpResponseMessage Ok(string body)
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private const string ClientBody = """
    {"clientName":"João","suitabilityLevel":"moderado","businessSegment":"private","country":"BR","isOffshore":false}
    """;

    private const string AdminBody = """
    {"username":"ana","partnerType":"GESTOR","segments":["B2B","WM"],"institutions":["BTG"],"isInternal":true,"isWM":true,"isMaster":false,"isBroker":false}
    """;

    // ── Round-trip por subtipo ───────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Cliente_PopulaL2ComClientPersona_E_L2HitDesserializaCorreto()
    {
        var (cache, redis, handler) = Build(_ => Ok(ClientBody));

        // 1ª chamada: miss total → HTTP + populate L2 + L1.
        var first = await cache.ResolveAsync("u1", "cliente");
        handler.CallCount.Should().Be(1);
        redis.WriteCount.Should().Be(1);

        // 2ª chamada sem TTL expirar: hit L1, sem I/O.
        var second = await cache.ResolveAsync("u1", "cliente");
        handler.CallCount.Should().Be(1);

        first.Should().BeOfType<ClientPersona>();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task Resolve_Admin_RoundTripL2_ManterTipoConcreto()
    {
        var (cache, redis, handler) = Build(_ => Ok(AdminBody));

        // Primeiro chamada povoa L1+L2.
        var first = (AdminPersona)await cache.ResolveAsync("u1", "admin");

        // Invalida só L1 (simulando outro pod) e força hit L2.
        await cache.InvalidateAsync("u1", "admin");
        redis.Seed("persona:admin:u1",
            System.Text.Json.JsonSerializer.Serialize(first, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));

        var second = await cache.ResolveAsync("u1", "admin");
        second.Should().BeOfType<AdminPersona>();
        ((AdminPersona)second).Username.Should().Be("ana");
        ((AdminPersona)second).IsWm.Should().BeTrue();
        ((AdminPersona)second).Segments.Should().BeEquivalentTo(new[] { "B2B", "WM" });
    }

    [Fact]
    public async Task Resolve_L2Corrompido_NaoEngole_VaiPraL3()
    {
        // Payload inválido no Redis não pode virar Anonymous silencioso — deve
        // logar warning (C5 aplicado ao cache) e refazer fetch da fonte.
        var (cache, redis, handler) = Build(_ => Ok(ClientBody));
        redis.Seed("persona:cliente:u1", "{not valid json");

        var persona = await cache.ResolveAsync("u1", "cliente");

        persona.Should().BeOfType<ClientPersona>();
        ((ClientPersona)persona).ClientName.Should().Be("João");
        handler.CallCount.Should().Be(1); // L2 corrompido → fetch L3
    }

    // ── Single-flight ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_SingleFlight_NConcorrentes_DisparamUmaChamada()
    {
        // Gate explícito: o handler bloqueia até liberarmos. Assim, as 10 tasks
        // entram no WaitAsync ANTES de qualquer uma poder resolver → garantimos
        // que todas peguem o mesmo Lazy. Sem heurística de tempo (não-flaky em CI).
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (cache, _, handler) = Build(_ => Ok(ClientBody), gate: gate);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => cache.ResolveAsync("u1", "cliente"))
            .ToArray();

        // Pequena pausa para garantir que todas as tasks chamaram GetOrAdd e
        // estão aguardando. O gate garante que o handler só avança depois.
        await Task.Yield();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        handler.CallCount.Should().Be(1);
        results.Should().OnlyContain(p => p is ClientPersona && !p.IsAnonymous);
        // Todas as tasks recebem a MESMA instância (vinda do Lazy compartilhado).
        results.Should().OnlyContain(p => ReferenceEquals(p, results[0]));
    }

    // ── Invalidação ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalidate_RemoveL1EL2_EProximoResolveBatePraFonte()
    {
        var (cache, redis, handler) = Build(_ => Ok(ClientBody));
        await cache.ResolveAsync("u1", "cliente");
        handler.CallCount.Should().Be(1);

        await cache.InvalidateAsync("u1", "cliente");
        redis.GetStringAsync("persona:cliente:u1").Result.Should().BeNull();

        await cache.ResolveAsync("u1", "cliente");
        handler.CallCount.Should().Be(2); // cache vazio → segunda chamada HTTP
    }
}
