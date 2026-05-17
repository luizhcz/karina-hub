using System.Text.Json;
using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Infra.Persistence.Cache;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Façade pra captura LLM. Lê estado do Redis (sub-ms) e cai pro DB quando
/// cache miss; toda escrita admin invalida o Redis pra propagar imediato.
///
/// Middleware <c>LlmInvocationCaptureChatClient</c> chama <see cref="GetCurrentAsync"/>
/// em cada turno — overhead é uma consulta Redis (~0.3-0.8ms) quando capture
/// está OFF. Quando ON, paga isso mais a serialização do payload.
/// </summary>
public sealed class LlmCaptureConfigService
{
    private const string CacheKey = "llm-capture:config";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ILlmCaptureConfigRepository _repo;
    private readonly IEfsRedisCache _cache;
    private readonly ILogger<LlmCaptureConfigService> _logger;

    public LlmCaptureConfigService(
        ILlmCaptureConfigRepository repo,
        IEfsRedisCache cache,
        ILogger<LlmCaptureConfigService> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<LlmCaptureConfig> GetCurrentAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetStringAsync(CacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LlmCaptureConfig>(cached);
                if (parsed is not null) return parsed;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[LlmCapture] Cache Redis com payload inválido — fallback ao DB.");
            }
        }

        var fromDb = await _repo.GetAsync(ct);
        await _cache.SetStringAsync(CacheKey, JsonSerializer.Serialize(fromDb), CacheTtl);
        return fromDb;
    }

    public async Task<LlmCaptureConfig> UpdateAsync(LlmCaptureConfig next, CancellationToken ct = default)
    {
        var saved = await _repo.UpsertAsync(next, ct);
        // Invalida pra próximo read trazer do DB. Alternativa "set direto" tem
        // race com expiry job — sempre re-read garante consistência.
        await _cache.RemoveAsync(CacheKey);
        _logger.LogInformation(
            "[LlmCapture] Config atualizada: Enabled={Enabled}, Expires={Expires}, By={By}",
            saved.Enabled, saved.ExpiresAt, saved.EnabledBy);
        return saved;
    }
}
