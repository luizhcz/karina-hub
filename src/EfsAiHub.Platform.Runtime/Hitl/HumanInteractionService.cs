using System.Collections.Concurrent;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Coordination;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Gerencia interações Human-in-the-Loop.
///
/// Estado em memória: dicionários para lookups rápidos e TaskCompletionSource para
/// pausar a execução do workflow até o humano responder.
///
/// Durabilidade: toda interação é persistida no PostgreSQL via IHumanInteractionRepository.
/// Em caso de restart, LoadPendingFromDbAsync() recarrega as interações Pending do banco
/// (sem TCS ativo — workflows que as aguardavam não estão mais em execução).
/// </summary>
public class HumanInteractionService : IHumanInteractionService
{
    private readonly ConcurrentDictionary<string, HumanInteractionRequest> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTcs = new();
    private readonly IHumanInteractionRepository _repo;
    private readonly ILogger<HumanInteractionService> _logger;
    private readonly ICrossNodeBus? _crossBus;

    public HumanInteractionService(
        IHumanInteractionRepository repo,
        ILogger<HumanInteractionService> logger,
        ICrossNodeBus? crossBus = null)
    {
        _repo = repo;
        _logger = logger;
        _crossBus = crossBus;
    }

    /// <summary>
    /// Carrega interações com Status=Pending do banco na inicialização do processo.
    /// Essas interações são "órfãs" — o workflow que as criou não está mais em execução.
    /// Ficam visíveis na API (/interactions/pending) até serem resolvidas ou expiradas.
    /// </summary>
    public async Task LoadPendingFromDbAsync(CancellationToken ct = default)
    {
        var stored = await _repo.GetPendingAsync(ct);
        foreach (var req in stored)
            _pending[req.InteractionId] = req;

        if (stored.Count > 0)
            _logger.LogWarning(
                "[HITL] {Count} interação(ões) pendente(s) recuperada(s) do banco após restart. " +
                "Os workflows que as aguardavam não estão mais em execução.",
                stored.Count);
    }

    /// <summary>
    /// Registra uma requisição de interação no banco e retorna uma Task que resolverá
    /// quando o humano responder (ou o CancellationToken for cancelado).
    /// </summary>
    public async Task<string> RequestAsync(HumanInteractionRequest request, CancellationToken ct = default)
    {
        await _repo.CreateAsync(request, ct);

        _pending[request.InteractionId] = request;
        MetricsRegistry.HitlRequested.Add(1,
            new KeyValuePair<string, object?>("workflow_id", request.WorkflowId));
        UpdatePendingAgeGauge();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingTcs[request.InteractionId] = tcs;

        // Timeout independente do HITL (não depende do workflow timeout)
        CancellationTokenSource? hitlTimeoutCts = null;
        CancellationTokenRegistration? hitlTimeoutReg = null;
        if (request.TimeoutSeconds > 0)
        {
            hitlTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            hitlTimeoutReg = hitlTimeoutCts.Token.Register(() =>
            {
                if (_pendingTcs.ContainsKey(request.InteractionId))
                {
                    _logger.LogWarning(
                        "[HITL] Interação '{InteractionId}' expirou após {Timeout}s (timeout HITL independente).",
                        request.InteractionId, request.TimeoutSeconds);
                    // Callback de CancellationToken é síncrono; Resolve é async após CAS →
                    // schedule fire-and-forget. Falha não deve propagar ao timer.
                    _ = Task.Run(async () =>
                    {
                        try { await ResolveAsync(request.InteractionId, "timeout", approved: false); }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "[HITL] Falha ao resolver por timeout a interação '{InteractionId}'.",
                                request.InteractionId);
                        }
                    });
                }
            });
        }

        ct.Register(() =>
        {
            if (_pendingTcs.TryRemove(request.InteractionId, out var t))
            {
                t.TrySetCanceled();
                _pending.TryRemove(request.InteractionId, out _);
                RecordResolution(request, "expired");

                // Persiste Expired no banco — sem o CT cancelado para não falhar imediatamente
                _ = Task.Run(async () =>
                {
                    try { await _repo.ExpireByExecutionIdAsync(request.ExecutionId); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[HITL] Falha ao expirar interação '{InteractionId}' no banco após cancelamento.",
                            request.InteractionId);
                    }
                });
            }
        });

        _logger.LogInformation("Interação HITL '{InteractionId}' aguardando resposta (timeout={Timeout}s).",
            request.InteractionId, request.TimeoutSeconds > 0 ? request.TimeoutSeconds : -1);

        try
        {
            return await tcs.Task;
        }
        finally
        {
            hitlTimeoutReg?.Dispose();
            hitlTimeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// Resolve uma interação pendente com a resposta do humano. CAS a nível de banco
    /// garante que duas chamadas concorrentes (ex: API + cross-pod NOTIFY) não corrompam
    /// o estado — apenas uma vence. Retorna false se já foi resolvido.
    /// </summary>
    public async Task<bool> ResolveAsync(
        string interactionId,
        string resolution,
        bool approved = true,
        bool publishToCross = true,
        CancellationToken ct = default)
    {
        if (!_pending.TryGetValue(interactionId, out var request))
            return false;

        var newStatus = approved ? HumanInteractionStatus.Approved : HumanInteractionStatus.Rejected;
        var resolvedAt = DateTime.UtcNow;

        // CAS no banco ANTES de tocar estado em memória. Se outro pod/caller já resolveu,
        // rowsAffected=0 e retornamos false sem mutar nada local.
        bool cas;
        try
        {
            cas = await _repo.TryResolveAsync(interactionId, newStatus, resolution, resolvedAt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[HITL] Falha no CAS de resolução da interação '{InteractionId}' — abortado.", interactionId);
            return false;
        }

        if (!cas)
        {
            MetricsRegistry.HitlResolveConflicts.Add(1,
                new KeyValuePair<string, object?>("outcome", approved ? "approved" : "rejected"));
            _logger.LogDebug(
                "[HITL] Resolução concorrente detectada para '{InteractionId}' — CAS retornou false; " +
                "outro caller/pod já resolveu.", interactionId);
            // Ainda assim limpa estado local — pode ter vindo cross-pod e só agora chegou aqui.
            CleanupLocalState(interactionId, resolution);
            return false;
        }

        // Venceu o CAS — aplica snapshot no objeto em memória para consistência local.
        request.Resolution = resolution;
        request.Status = newStatus;
        request.ResolvedAt = resolvedAt;

        RecordResolution(request, approved ? "approved" : "rejected");
        CleanupLocalState(interactionId, resolution);

        _logger.LogInformation(
            "Interação HITL '{InteractionId}' resolvida (approved={Approved}).",
            interactionId, approved);

        // Propaga para outros pods só quando efetivamente resolvemos (evita loop de NOTIFY).
        if (publishToCross && _crossBus is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await _crossBus.PublishHitlResolvedAsync(interactionId, resolution, approved); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[HITL] Falha ao publicar resolução cross-pod de '{InteractionId}'.", interactionId);
                }
            });
        }

        return true;
    }

    private void CleanupLocalState(string interactionId, string resolution)
    {
        _pending.TryRemove(interactionId, out _);
        UpdatePendingAgeGauge();

        if (_pendingTcs.TryRemove(interactionId, out var tcs))
            tcs.TrySetResult(resolution);
    }

    /// <summary>
    /// Re-registra um TaskCompletionSource para uma interação HITL previamente
    /// carregada do banco (após restart). Usado pelo HitlRecoveryService para
    /// retomar a espera do workflow por resposta humana.
    /// Retorna null se a interação não está no dicionário _pending.
    /// </summary>
    public TaskCompletionSource<string>? ReRegisterPending(string interactionId)
    {
        if (!_pending.ContainsKey(interactionId))
        {
            _logger.LogWarning(
                "[HITL] ReRegisterPending: interação '{InteractionId}' não está no cache _pending.",
                interactionId);
            return null;
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingTcs[interactionId] = tcs;
        _logger.LogInformation("[HITL] Interação '{InteractionId}' re-registrada após restart.", interactionId);
        return tcs;
    }

    /// <summary>
    /// Injeta uma interação HITL (qualquer status) no cache em memória.
    /// Usado pelo HitlRecoveryService para preparar HITLs já resolvidos (Approved/Rejected)
    /// antes de chamar ReRegisterPending + SetResult.
    /// </summary>
    public void InjectForRecovery(HumanInteractionRequest request)
    {
        _pending[request.InteractionId] = request;
    }

    /// <summary>
    /// Expira todas as interações Pending de uma execução que foi cancelada ou falhada.
    /// Cobre tanto HITLs com TCS ativo quanto HITLs órfãs carregadas do banco após restart.
    /// </summary>
    public async Task ExpireForExecutionAsync(string executionId)
    {
        // Remove do cache em memória e cancela TCS ativos
        var toExpire = _pending.Values
            .Where(r => r.ExecutionId == executionId && r.Status == HumanInteractionStatus.Pending)
            .ToList();

        foreach (var req in toExpire)
        {
            _pending.TryRemove(req.InteractionId, out _);
            if (_pendingTcs.TryRemove(req.InteractionId, out var tcs))
                tcs.TrySetCanceled();
        }

        // Persiste no banco (inclui HITLs que nunca tiveram TCS ativo pois foram recarregadas do DB)
        try { await _repo.ExpireByExecutionIdAsync(executionId); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[HITL] Falha ao expirar interações da execução '{ExecutionId}' no banco.", executionId);
        }

        if (toExpire.Count > 0)
            _logger.LogInformation(
                "[HITL] {Count} interação(ões) expirada(s) para execução cancelada '{ExecutionId}'.",
                toExpire.Count, executionId);
    }

    public IReadOnlyList<HumanInteractionRequest> GetPending()
        => _pending.Values.ToList();

    public HumanInteractionRequest? GetById(string interactionId)
        => _pending.TryGetValue(interactionId, out var req) ? req : null;

    public IReadOnlyList<HumanInteractionRequest> GetByExecutionId(string executionId)
        => _pending.Values.Where(r => r.ExecutionId == executionId).ToList();

    /// <summary>
    /// Retorna a primeira interação com Status == Pending para o executionId, ou null.
    /// Usado pela camada de chat para detectar se o próximo turno deve resolver um HITL.
    /// </summary>
    public HumanInteractionRequest? GetPendingForExecution(string executionId)
        => _pending.Values.FirstOrDefault(r =>
            r.ExecutionId == executionId &&
            r.Status == HumanInteractionStatus.Pending);

    private static void RecordResolution(HumanInteractionRequest request, string outcome)
    {
        MetricsRegistry.HitlResolved.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome));

        var duration = (DateTime.UtcNow - request.CreatedAt).TotalSeconds;
        MetricsRegistry.HitlResolutionDuration.Record(duration,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private void UpdatePendingAgeGauge()
    {
        var oldest = _pending.Values
            .Where(r => r.Status == HumanInteractionStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefault();

        var ageSeconds = oldest is not null
            ? (long)(DateTime.UtcNow - oldest.CreatedAt).TotalSeconds
            : 0;

        MetricsRegistry.SetHitlPendingAgeSeconds(ageSeconds);
    }
}
