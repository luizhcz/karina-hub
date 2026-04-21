using System.ComponentModel;
using System.Text.Json;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Host.Api.CodeExecutors;

/// <summary>
/// Registra tools mock para o workflow de teste "resgate-investimento".
/// Simula integração com sistemas de custódia, cálculo tributário e processamento de ordens.
/// build_redemption_order inclui HITL: bloqueia até aprovação humana via HumanInteractionService.
/// </summary>
public static class RedemptionTestSetup
{
    // Singletons capturados no startup — injetados via RegisterRedemptionTools.
    private static IHumanInteractionService? _hitlService;
    private static IWorkflowEventBus? _eventBus;

    public static void RegisterRedemptionTools(this WebApplication app)
    {
        _hitlService = app.Services.GetRequiredService<IHumanInteractionService>();
        _eventBus   = app.Services.GetRequiredService<IWorkflowEventBus>();

        var registry = app.Services.GetRequiredService<IFunctionToolRegistry>();

        registry.Register("get_client_position",   AIFunctionFactory.Create(GetClientPosition,   new AIFunctionFactoryOptions { Name = "get_client_position" }));
        registry.Register("calculate_tax",          AIFunctionFactory.Create(CalculateTax,         new AIFunctionFactoryOptions { Name = "calculate_tax" }));
        registry.Register("get_fund_details",       AIFunctionFactory.Create(GetFundDetails,       new AIFunctionFactoryOptions { Name = "get_fund_details" }));
        registry.Register("get_market_data",        AIFunctionFactory.Create(GetMarketData,        new AIFunctionFactoryOptions { Name = "get_market_data" }));
        registry.Register("build_redemption_order", AIFunctionFactory.Create(BuildRedemptionOrder, new AIFunctionFactoryOptions { Name = "build_redemption_order" }));
        registry.Register("execute_redemption",     AIFunctionFactory.Create(ExecuteRedemption,    new AIFunctionFactoryOptions { Name = "execute_redemption" }));
        registry.Register("generate_receipt",       AIFunctionFactory.Create(GenerateReceipt,      new AIFunctionFactoryOptions { Name = "generate_receipt" }));
    }

    [Description("Busca posição atual do cliente em um fundo")]
    private static string GetClientPosition(
        [Description("Conta do cliente")] string account,
        [Description("Nome do fundo")] string fund)
    {
        return JsonSerializer.Serialize(new
        {
            account,
            fund,
            balance      = 120_000.00m,
            shares       = 118_523.45m,
            averagePrice = 1.0125m,
            startDate    = "2024-06-15",
            holdingDays  = 665,
            currency     = "BRL"
        });
    }

    [Description("Calcula IR sobre resgate considerando tabela regressiva")]
    private static string CalculateTax(
        [Description("Valor bruto do resgate")] decimal amount,
        [Description("Dias de permanência no fundo")] int holdingDays)
    {
        var rate = holdingDays switch
        {
            <= 180 => 0.225m,
            <= 360 => 0.20m,
            <= 720 => 0.175m,
            _      => 0.15m
        };
        var taxAmount = Math.Round(amount * rate, 2);
        return JsonSerializer.Serialize(new
        {
            taxRate     = rate,
            taxAmount,
            netAmount   = amount - taxAmount,
            holdingDays,
            bracket     = holdingDays switch
            {
                <= 180 => "até 180 dias (22.5%)",
                <= 360 => "181-360 dias (20%)",
                <= 720 => "361-720 dias (17.5%)",
                _      => "acima de 720 dias (15%)"
            }
        });
    }

    [Description("Retorna detalhes de um fundo de investimento")]
    private static string GetFundDetails(
        [Description("Nome do fundo")] string fund)
    {
        return JsonSerializer.Serialize(new
        {
            fund,
            fullName       = "EFS Referenciado DI FI",
            type           = "Renda Fixa - Referenciado DI",
            liquidation    = "D+1",
            managementFee  = 0.005m,
            minimumBalance = 1_000.00m,
            ytdReturn      = 0.0892m,
            benchmarkCdi   = 0.0901m,
            aum            = 450_000_000.00m
        });
    }

    [Description("Retorna dados de mercado atuais")]
    private static string GetMarketData()
    {
        return JsonSerializer.Serialize(new
        {
            cdiRate         = 0.1065m,
            selicRate       = 0.1075m,
            ipcaAccumulated = 0.0445m,
            usdBrl          = 5.12m,
            date            = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd")
        });
    }

    /// <summary>
    /// Monta a ordem de resgate e aguarda aprovação humana (HITL) antes de retornar.
    /// Publica "hitl_required" → SSE envia TOOL_CALL_START(request_approval) → frontend mostra botões.
    /// A execução fica bloqueada em _hitlService.RequestAsync até que /resolve-hitl seja chamado.
    /// </summary>
    [Description("Monta ordem de resgate com todos os dados necessários e solicita aprovação humana")]
    private static async Task<string> BuildRedemptionOrder(
        [Description("Conta do cliente")]               string account,
        [Description("Fundo para resgate")]             string fund,
        [Description("Valor bruto do resgate")]         decimal amount,
        [Description("Alíquota de IR aplicável")]       decimal taxRate,
        [Description("Valor líquido após IR")]          decimal netAmount,
        [Description("Prazo de liquidação (ex: D+1)")]  string liquidationDate,
        [Description("Banco destino para crédito")]     string recipientBank,
        [Description("Conta destino para crédito")]     string recipientAccount,
        CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8].ToUpper();

        var ctx = DelegateExecutor.Current.Value;
        if (ctx != null && _hitlService != null && _eventBus != null)
        {
            var interactionId = Guid.NewGuid().ToString();
            var prompt = $"Confirmar resgate de {amount:C} do fundo '{fund}'? " +
                         $"Valor líquido: {netAmount:C} | Liquidação: {liquidationDate} | " +
                         $"Crédito em: {recipientBank} / {recipientAccount}";

            // Publica hitl_required → AgUiEventMapper converte em TOOL_CALL_START(request_approval)
            await _eventBus.PublishAsync(ctx.ExecutionId, new WorkflowEventEnvelope
            {
                EventType  = "hitl_required",
                ExecutionId = ctx.ExecutionId,
                Payload    = JsonSerializer.Serialize(new
                {
                    interactionId,
                    prompt,
                    question       = prompt,
                    orderId,
                    options        = new[] { "Aprovar", "Rejeitar" },
                    timeoutSeconds = 300
                })
            }, ct);

            // Aguarda resolução humana — bloqueia até que /resolve-hitl seja chamado
            var resolution = await _hitlService.RequestAsync(new HumanInteractionRequest
            {
                InteractionId   = interactionId,
                ExecutionId     = ctx.ExecutionId,
                WorkflowId      = ctx.WorkflowId,
                Prompt          = prompt,
                Context         = ctx.Input,
                InteractionType = InteractionType.Approval,
                Options         = new[] { "Aprovar", "Rejeitar" },
                TimeoutSeconds  = 180
            }, ct);

            if (HitlResolutionClassifier.IsRejected(resolution))
                return JsonSerializer.Serialize(new
                {
                    status     = "rejected",
                    orderId,
                    message    = "Resgate cancelado pelo cliente.",
                    resolution
                });

            return JsonSerializer.Serialize(new
            {
                orderId,
                account,
                fund,
                amount,
                taxRate,
                netAmount,
                liquidationDate,
                recipientBank,
                recipientAccount,
                status              = "approved",
                createdAt           = DateTimeOffset.UtcNow,
                estimatedCreditDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
                approvalNote        = resolution
            });
        }

        // Fallback sem HITL (testes unitários / ambiente sem serviços)
        return JsonSerializer.Serialize(new
        {
            orderId,
            account,
            fund,
            amount,
            taxRate,
            netAmount,
            liquidationDate,
            recipientBank,
            recipientAccount,
            status              = "pending_approval",
            createdAt           = DateTimeOffset.UtcNow,
            estimatedCreditDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")
        });
    }

    [Description("Executa a ordem de resgate no sistema")]
    private static string ExecuteRedemption(
        [Description("ID da ordem aprovada")] string orderId)
    {
        var txId = $"TX{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
        return JsonSerializer.Serialize(new
        {
            orderId,
            transactionId = txId,
            status        = "executed",
            executedAt    = DateTimeOffset.UtcNow
        });
    }

    [Description("Gera comprovante do resgate")]
    private static string GenerateReceipt(
        [Description("ID da transação executada")] string transactionId)
    {
        return JsonSerializer.Serialize(new
        {
            transactionId,
            receiptUrl  = $"/receipts/{transactionId}.pdf",
            generatedAt = DateTimeOffset.UtcNow
        });
    }
}
