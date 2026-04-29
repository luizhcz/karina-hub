using System.ComponentModel;
using System.Text.Json;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Function tool que solicita confirmação humana (HITL) para uma boleta.
/// Registrada como AIFunction no IFunctionToolRegistry — invocada pelo agente coletor
/// quando todos os campos foram coletados.
///
/// Publica hitl_required → frontend mostra ApprovalBubble com botões Confirmar/Cancelar.
/// Bloqueia até resolução via /resolve-hitl.
///
/// Retorno:
///   confirmado  → {"confirmed":true,"message":"..."}
///   cancelado   → {"confirmed":false,"message":"Ordem cancelada pelo usuário."}
/// </summary>
public static class ConfirmBoletaFunction
{
    private static IHumanInteractionService? _hitlService;
    private static IWorkflowEventBus? _eventBus;

    /// <summary>
    /// Deve ser chamado em Program.cs para injetar os serviços necessários
    /// e registrar a tool no IFunctionToolRegistry.
    /// </summary>
    public static void Configure(
        IHumanInteractionService hitlService,
        IWorkflowEventBus eventBus,
        IFunctionToolRegistry registry)
    {
        _hitlService = hitlService;
        _eventBus = eventBus;

        registry.Register("confirm_boleta",
            AIFunctionFactory.Create(ConfirmBoleta,
                new AIFunctionFactoryOptions { Name = "confirm_boleta" }));
    }

    [Description("Solicita confirmação humana para uma ordem de boleta. Chame SOMENTE quando todos os campos estiverem coletados (pronto=true).")]
    private static async Task<string> ConfirmBoleta(
        [Description("Tipo da operação: compra ou venda")] string tipoOperacao,
        [Description("Ticker do ativo (ex: PETR4)")] string ticker,
        [Description("Quantidade de ações")] int quantidade,
        [Description("Tipo de preço: M (mercado), L (limitado), F (financeiro)")] string tipoPreco,
        [Description("Valor em BRL (preço limite ou volume). Null para mercado.")] decimal? valorBrl = null,
        CancellationToken ct = default)
    {
        var tipoLabel = tipoOperacao.Equals("venda", StringComparison.OrdinalIgnoreCase) ? "VENDA" : "COMPRA";
        var tipoPrecoLabel = tipoPreco switch
        {
            "L" => "Limitado",
            "F" => "Financeiro",
            _   => "Mercado"
        };

        // ── Montar prompt descritivo ──
        var prompt = $"Confirmar ordem de {tipoLabel} — " +
                     $"Ativo: {ticker} | " +
                     $"Quantidade: {quantidade} | " +
                     $"Tipo de Preço: {tipoPrecoLabel}";

        if (valorBrl.HasValue)
        {
            var valorLabel = tipoPreco == "F" ? "Volume Total" : "Preço Limite";
            prompt += $" | {valorLabel}: R$ {valorBrl.Value:N2}";
        }

        // ── Publicar hitl_required e bloquear ──
        var ctx = DelegateExecutor.Current.Value;
        if (ctx == null || _hitlService == null || _eventBus == null)
            return JsonSerializer.Serialize(new { confirmed = false, message = "Serviço HITL indisponível." });

        var interactionId = Guid.NewGuid().ToString();

        await _eventBus.PublishAsync(ctx.ExecutionId, new WorkflowEventEnvelope
        {
            EventType   = "hitl_required",
            ExecutionId = ctx.ExecutionId,
            Payload     = JsonSerializer.Serialize(new
            {
                interactionId,
                prompt,
                question       = prompt,
                options        = new[] { "Confirmar", "Cancelar" },
                timeoutSeconds = 180
            })
        }, CancellationToken.None);

        var resolution = await _hitlService.RequestAsync(new HumanInteractionRequest
        {
            InteractionId  = interactionId,
            ExecutionId    = ctx.ExecutionId,
            WorkflowId     = ctx.WorkflowId,
            Prompt         = prompt,
            Context        = ctx.Input,
            InteractionType = InteractionType.Approval,
            Options        = new[] { "Confirmar", "Cancelar" },
            TimeoutSeconds = 180
        }, ct);

        // ── Interpretar resposta ──
        if (HitlResolutionClassifier.IsRejected(resolution))
            return JsonSerializer.Serialize(new { confirmed = false, message = "Ordem cancelada pelo usuário." });

        return JsonSerializer.Serialize(new
        {
            confirmed = true,
            message = $"Ordem confirmada — {tipoLabel} {quantidade} {ticker} ({tipoPrecoLabel}" +
                      (valorBrl.HasValue ? $" R$ {valorBrl.Value:N2}" : "") + ")."
        });
    }
}
