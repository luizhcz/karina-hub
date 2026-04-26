using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Blocklist;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Messaging;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Guards.BuiltIns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Resolve o <see cref="BlocklistMatcher"/> efetivo de um projeto, combinando o catálogo
/// curado (compartilhado, cacheado em L2 Redis) com o override do projeto
/// (<see cref="BlocklistSettings"/>, recompilado em L1 in-memory).
///
/// Cache híbrido:
/// <list type="bullet">
///   <item>L1 (in-process): matcher compilado por projectId, TTL 5s ± jitter</item>
///   <item>L2 (Redis): snapshot serializado do catálogo, compartilhado entre pods, TTL 60s ± jitter</item>
///   <item>NOTIFY 'blocklist_changed': invalida L1+L2 imediatamente cross-pod (fallback do TTL)</item>
/// </list>
///
/// Single-flight per projectId via SemaphoreSlim — múltiplas requests concorrentes que
/// missem a cache compartilham o mesmo build do matcher.
///
/// Failure mode: load do catálogo falhou? Mantém matcher antigo do L1 e loga (fail-safe).
/// Nunca opera sem matcher por causa de fetch errado — mas também nunca rejeita request
/// silenciosamente (fail-open só acontece se o L1 nunca foi populado para o projeto).
/// </summary>
// Não-sealed pra permitir subclasse de teste com override de GetMatcherAsync.
// Em produção, há apenas 1 instância via DI Singleton — herança não é caminho de extensão de domínio.
public class BlocklistEngine : IHostedService, IDisposable
{
    private const string CatalogCacheKey = "blocklist:catalog";
    /// <summary>cacheName usado pelo BlocklistController quando admin atualiza ProjectSettings.Blocklist.</summary>
    public const string ProjectInvalidationCacheName = "blocklist-project";
    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan L2Ttl = TimeSpan.FromSeconds(60);

    private readonly IBlocklistCatalogRepository _catalogRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IEfsRedisCache _l2Cache;
    private readonly PgNotifyDispatcher _dispatcher;
    private readonly ICacheInvalidationBus? _cacheBus;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyDictionary<string, IBuiltInPatternHandler> _builtIns;
    private readonly ILogger<BlocklistEngine> _logger;

    // L1: matcher compilado por projectId. Tupla (matcher, expiresAtUtc) — checagem manual de TTL.
    private readonly ConcurrentDictionary<string, (BlocklistMatcher Matcher, DateTime ExpiresAt)> _l1 = new();

    // Single-flight: 1 SemaphoreSlim por projectId pra evitar thundering herd no rebuild.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    private IDisposable? _notifySubscription;
    private IDisposable? _projectInvalidationSubscription;

    public BlocklistEngine(
        IBlocklistCatalogRepository catalogRepo,
        IProjectRepository projectRepo,
        IEfsRedisCache l2Cache,
        PgNotifyDispatcher dispatcher,
        IEnumerable<IBuiltInPatternHandler> builtIns,
        ILogger<BlocklistEngine> logger,
        IServiceProvider services,
        ICacheInvalidationBus? cacheBus = null)
    {
        _catalogRepo = catalogRepo;
        _projectRepo = projectRepo;
        _l2Cache = l2Cache;
        _dispatcher = dispatcher;
        _cacheBus = cacheBus;
        _services = services;
        _builtIns = builtIns.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fail-fast: SAFETY_VIOLATION em streaming exige IWorkflowEventBus pra publicar
        // no canal SSE. Se não estiver registrado, cliente vê HTTP 422 mas SSE não fecha
        // com terminal event — falha silenciosa que demora dias pra ser detectada.
        // Throw aqui (startup) é melhor que descobrir em produção via violação real.
        var eventBus = _services.GetService<IWorkflowEventBus>();
        if (eventBus is null)
        {
            _logger.LogCritical(
                "[BlocklistEngine] IWorkflowEventBus não registrado no DI. SAFETY_VIOLATION " +
                "nunca seria emitido em SSE — cliente veria HTTP 422 mas sem evento terminal. " +
                "Registre o messaging (PgEventBus) antes do BlocklistEngine no Program.cs.");
            throw new InvalidOperationException(
                "BlocklistEngine requer IWorkflowEventBus registrado no container DI.");
        }

        _notifySubscription = _dispatcher.SubscribeBlocklistChanged(InvalidateAllAsync);
        _logger.LogInformation("[BlocklistEngine] Subscrito ao canal NOTIFY 'blocklist_changed'.");

        // Invalidação cross-pod por projeto (BlocklistController.PUT publica aqui).
        if (_cacheBus is not null)
        {
            _projectInvalidationSubscription = _cacheBus.Subscribe(
                ProjectInvalidationCacheName,
                projectId => { InvalidateProject(projectId); return Task.CompletedTask; });
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _notifySubscription?.Dispose();
        _notifySubscription = null;
        _projectInvalidationSubscription?.Dispose();
        _projectInvalidationSubscription = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _notifySubscription?.Dispose();
        _projectInvalidationSubscription?.Dispose();
    }

    /// <summary>
    /// Invalida o L1 de um projeto específico. Chamado localmente após PUT /api/projects/{id}/blocklist
    /// (impacto imediato no pod corrente) e via subscribe do <see cref="ICacheInvalidationBus"/>
    /// (impacto cross-pod). Próxima request rebuilda o matcher com a nova config.
    /// <para>
    /// Race possível: outra thread está dentro do lock com o semaphore antigo. Sem problema —
    /// ela completa o build com config "stale" mas o resultado é descartado quando ela tenta
    /// guardar em L1 (próxima request remove de novo via TryRemove). Próximo GetMatcherAsync
    /// cria semaphore novo on-demand.
    /// </para>
    /// </summary>
    public void InvalidateProject(string projectId)
    {
        _l1.TryRemove(projectId, out _);
        _semaphores.TryRemove(projectId, out _);
        _logger.LogDebug("[BlocklistEngine] L1 invalidado para projeto '{ProjectId}'.", projectId);
    }

    /// <summary>
    /// Retorna o matcher efetivo do projeto. Hot-path — chamado a cada turno LLM
    /// pelo BlocklistChatClient (PR 6). L1 hit é o caso normal (~99%); miss aciona
    /// rebuild com single-flight.
    /// <para>
    /// <c>virtual</c> pra permitir override em testes sem mock pesado de todas as deps
    /// (catalog repo, project repo, redis, dispatcher). Subclasse de teste retorna
    /// matcher pré-construído via <see cref="BlocklistMatcher.Build"/>.
    /// </para>
    /// </summary>
    public virtual async Task<BlocklistMatcher> GetMatcherAsync(string projectId, CancellationToken ct = default)
    {
        if (TryGetFromL1(projectId, out var cached))
        {
            MetricsRegistry.BlocklistCacheHits.Add(1, new KeyValuePair<string, object?>("layer", "l1"));
            return cached;
        }

        var sem = _semaphores.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Re-check L1 dentro do lock: outra request rebuilda enquanto esperávamos.
            // NÃO emite l1 hit aqui — o request atual é semanticamente cache miss
            // (entrou no caminho lock); o re-check só evita rebuild redundante.
            // Inflar hit ratio aqui mascararia a real eficiência do L1.
            if (TryGetFromL1(projectId, out cached))
                return cached;

            var matcher = await BuildMatcherAsync(projectId, ct);
            StoreInL1(projectId, matcher);
            return matcher;
        }
        finally
        {
            sem.Release();
        }
    }

    private bool TryGetFromL1(string projectId, out BlocklistMatcher matcher)
    {
        if (_l1.TryGetValue(projectId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            matcher = entry.Matcher;
            return true;
        }
        matcher = BlocklistMatcher.Empty;
        return false;
    }

    private void StoreInL1(string projectId, BlocklistMatcher matcher)
    {
        // Jitter ±10% pra evitar refresh sincronizado entre pods.
        var jitter = (Random.Shared.NextDouble() - 0.5) * 0.2;
        var ttl = L1Ttl + TimeSpan.FromMilliseconds(L1Ttl.TotalMilliseconds * jitter);
        _l1[projectId] = (matcher, DateTime.UtcNow.Add(ttl));
    }

    private async Task<BlocklistMatcher> BuildMatcherAsync(string projectId, CancellationToken ct)
    {
        BlocklistCatalogSnapshot catalog;
        try
        {
            catalog = await GetCatalogAsync(ct);
        }
        catch (Exception ex)
        {
            MetricsRegistry.BlocklistLoadErrors.Add(1);
            // Failure mode: mantém matcher antigo do L1 se houver. Nunca derruba o pipeline
            // por load do catálogo falhar — operacional pode investigar via warning log.
            _logger.LogWarning(ex,
                "[BlocklistEngine] Falha ao carregar catálogo para projeto '{ProjectId}'. " +
                "Reutilizando última versão do L1 se existir.", projectId);
            if (_l1.TryGetValue(projectId, out var stale))
                return stale.Matcher;
            return BlocklistMatcher.Empty;
        }

        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        var settings = project?.Settings?.Blocklist ?? BlocklistSettings.Default;

        if (!settings.Enabled)
            return BlocklistMatcher.Empty;

        var effective = ResolveEffectivePatterns(catalog, settings);
        return BlocklistMatcher.Build(effective, _builtIns, settings.Replacement, _logger);
    }

    private async Task<BlocklistCatalogSnapshot> GetCatalogAsync(CancellationToken ct)
    {
        var cached = await _l2Cache.GetStringAsync(CatalogCacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<BlocklistCatalogSnapshot>(cached, JsonDefaults.Domain);
                if (snapshot is not null)
                {
                    MetricsRegistry.BlocklistCacheHits.Add(1, new KeyValuePair<string, object?>("layer", "l2"));
                    return snapshot;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[BlocklistEngine] L2 cache do catálogo malformado — refazendo fetch.");
            }
        }

        var fresh = await _catalogRepo.LoadAllAsync(ct);
        var jitter = (Random.Shared.NextDouble() - 0.5) * 0.2;
        var ttl = L2Ttl + TimeSpan.FromMilliseconds(L2Ttl.TotalMilliseconds * jitter);
        await _l2Cache.SetStringAsync(
            CatalogCacheKey,
            JsonSerializer.Serialize(fresh, JsonDefaults.Domain),
            ttl);
        return fresh;
    }

    private static IReadOnlyCollection<EffectivePattern> ResolveEffectivePatterns(
        BlocklistCatalogSnapshot catalog,
        BlocklistSettings settings)
    {
        var result = new List<EffectivePattern>(catalog.Patterns.Count);
        var groupsById = catalog.Groups.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in catalog.Patterns)
        {
            if (!groupsById.TryGetValue(pattern.GroupId, out var group)) continue;

            BlocklistGroupOverride? groupOverride = null;
            settings.Groups?.TryGetValue(group.Id, out groupOverride);

            // Grupo desligado pelo projeto?
            if (groupOverride is { Enabled: false }) continue;

            // Pattern em disabled_patterns?
            if (groupOverride?.DisabledPatterns is { Count: > 0 } disabled
                && disabled.Contains(pattern.Id, StringComparer.OrdinalIgnoreCase))
                continue;

            var action = groupOverride?.ActionOverride ?? pattern.DefaultAction;
            result.Add(new EffectivePattern(pattern, group.Id, action));
        }

        // Custom patterns do projeto — encarados como categoria virtual "CUSTOM".
        if (settings.CustomPatterns is { Count: > 0 })
        {
            foreach (var custom in settings.CustomPatterns)
            {
                var synthetic = new BlocklistPattern(
                    Id: $"custom.{custom.Id}",
                    GroupId: "CUSTOM",
                    Type: custom.Type,
                    Pattern: custom.Pattern,
                    Validator: BlocklistValidator.None,
                    WholeWord: custom.WholeWord,
                    CaseSensitive: custom.CaseSensitive,
                    DefaultAction: custom.Action,
                    Enabled: true,
                    Version: 0);
                result.Add(new EffectivePattern(synthetic, "CUSTOM", custom.Action));
            }
        }

        return result;
    }

    private async Task InvalidateAllAsync()
    {
        _l1.Clear();
        // Cleanup dos semaphores junto pra não acumular SemaphoreSlim de projetos
        // que talvez nunca mais venham. Reabre on-demand no próximo GetMatcherAsync.
        _semaphores.Clear();
        try { await _l2Cache.RemoveAsync(CatalogCacheKey); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BlocklistEngine] Falha ao limpar L2 — TTL cobre fallback.");
        }
        _logger.LogInformation("[BlocklistEngine] Catálogo invalidado via NOTIFY 'blocklist_changed'.");
    }
}
