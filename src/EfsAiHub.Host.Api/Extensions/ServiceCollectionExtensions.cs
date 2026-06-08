using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Host.Api.CodeExecutors;
using EfsAiHub.Host.Worker.Services;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Infra.Persistence.CheckpointStore;
using EfsAiHub.Infra.Persistence.Postgres;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using StackExchange.Redis;

namespace EfsAiHub.Host.Api.Extensions;

public static class ServiceCollectionExtensions
{
    // ── Agent Middleware Registry ────────────────────────────────────────────────
    public static IServiceCollection AddAgentMiddlewareRegistry(this IServiceCollection services)
    {
        services.AddSingleton<EfsAiHub.Core.Agents.Interfaces.IAgentMiddlewareRegistry>(sp =>
        {
            var registry = new EfsAiHub.Platform.Runtime.Factories.AgentMiddlewareRegistry();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AgentMiddlewareRegistry");

            registry.Register("AccountGuard", MiddlewarePhase.Both,
                (inner, agentId, settings, _) =>
                    new EfsAiHub.Platform.Runtime.Middlewares.AccountGuardChatClient(inner, agentId, settings, logger),
                label: "Account Guard",
                description: "Controla o acesso a operações sensíveis de conta. Pode bloquear chamadas (ClientLocked) ou apenas logar (AdminLogOnly).",
                settings:
                [
                    new MiddlewareSettingDef
                    {
                        Key = "mode",
                        Label = "Modo",
                        Type = "select",
                        Options =
                        [
                            new MiddlewareSettingOption { Value = "ClientLocked", Label = "Client Locked" },
                            new MiddlewareSettingOption { Value = "AdminLogOnly", Label = "Admin Log Only" },
                        ],
                        DefaultValue = "ClientLocked",
                    },
                ]);

            registry.Register("StructuredOutputState", MiddlewarePhase.Post,
                (inner, agentId, settings, _) =>
                    new EfsAiHub.Platform.Runtime.Middlewares.StructuredOutputStateChatClient(inner, agentId, settings, logger),
                label: "Structured Output State",
                description: "Atualiza automaticamente o shared state (AG-UI) a partir do output estruturado do agente após cada resposta.");

            registry.Register("SecurityGuardrails", MiddlewarePhase.Pre,
                (inner, agentId, settings, _) =>
                    new EfsAiHub.Platform.Runtime.Middlewares.SecurityGuardrailsChatClient(inner, agentId, settings, logger),
                label: "Guardrails de segurança",
                description: "Injeta uma safety policy fixa (anti prompt injection, scope adherence, anti hallucination, anti leakage) como system message a cada chamada.",
                settings: []);

            registry.Register("RouterDecisionTelemetry", MiddlewarePhase.Post,
                (inner, agentId, settings, _) =>
                    new EfsAiHub.Platform.Runtime.Middlewares.RouterDecisionTelemetryChatClient(inner, agentId, settings, logger),
                label: "Telemetria de decisão do Router",
                description: "Emite métricas OTel por decisão do Router (router.decisions_total, router.confidence, router.ambiguity_signals_total) + log estruturado por turno. Aplica validação dura: needs_clarification sem candidate_intents válido é reescrito como out_of_scope antes de ir pro downstream.",
                settings: []);

            return registry;
        });

        return services;
    }

    // ── Function Tool Registry ──────────────────────────────────────────────────
    public static IServiceCollection AddFunctionToolRegistry(this IServiceCollection services)
    {
        // Tools com dependências (auth context, HttpClient etc.) registradas
        // como Singleton — o estado per-request flui via AsyncLocal nos accessors.
        services.AddOptions<EfsAiHub.Platform.Runtime.Tools.PortfolioApiOptions>()
            .BindConfiguration(EfsAiHub.Platform.Runtime.Tools.PortfolioApiOptions.SectionName);
        services.AddSingleton<EfsAiHub.Platform.Runtime.Tools.PortfolioAnalysisTool>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Tools.ClientPositionsTool>();

        services.AddSingleton<IFunctionToolRegistry>(sp =>
        {
            var registry = new FunctionToolRegistry(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<FunctionToolRegistry>());

            registry.Register("get_datetime", AIFunctionFactory.Create(DateTimeFunctions.GetDateTime));
            registry.Register("get_asset", AIFunctionFactory.Create(AssetFunctions.GetAsset));

            var portfolio = sp.GetRequiredService<EfsAiHub.Platform.Runtime.Tools.PortfolioAnalysisTool>();
            registry.Register("analyze_portfolio", AIFunctionFactory.Create(portfolio.AnalyzePortfolioAsync));

            var positions = sp.GetRequiredService<EfsAiHub.Platform.Runtime.Tools.ClientPositionsTool>();
            registry.Register("get_portfolio", AIFunctionFactory.Create(positions.GetPortfolioAsync));
            registry.Register("get_position",  AIFunctionFactory.Create(positions.GetPositionAsync));

            return registry;
        });

        services.AddSingleton<EfsAiHub.Core.Orchestration.Routing.IEscalationRouter>(sp =>
            new EfsAiHub.Core.Orchestration.Routing.EscalationRouter(
                sp.GetRequiredService<ILogger<EfsAiHub.Core.Orchestration.Routing.EscalationRouter>>(),
                (category, routed) => EfsAiHub.Infra.Observability.MetricsRegistry
                    .EscalationSignalsTotal.Add(1,
                        new KeyValuePair<string, object?>("category", category ?? "<none>"),
                        new KeyValuePair<string, object?>("routed", routed))));

        return services;
    }

    // ── Code Executor Registry ──────────────────────────────────────────────────
    public static IServiceCollection AddCodeExecutorRegistry(this IServiceCollection services)
    {
        services.AddSingleton<ICodeExecutorRegistry>(sp =>
        {
            var registry = new CodeExecutorRegistry(
                sp.GetRequiredService<ILogger<CodeExecutorRegistry>>());

            registry.Register("search_single", (input, ct) => WebSearchBatchFunctions.SearchSingle(input, ct));

            registry.Register("service_pre_processor", (input, ct) => ServicePreProcessor.EnrichInput(input, ct));

            // service_post_processor — input chega em duas formas (envelope agent/legacy flat),
            // por isso a entrada continua untyped. Saída é tipada em PostProcessorResult pra
            // habilitar predicate Switch sobre $.hasErrors.
            var postProcessorJsonOpts = new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web);
            registry.Register("service_post_processor", async (input, ct) =>
            {
                var result = await ServicePostProcessor.ValidateAndEnrichTyped(input, ct);
                return System.Text.Json.JsonSerializer.Serialize(result, postProcessorJsonOpts);
            });
            registry.RegisterSchema("service_post_processor", typeof(string), typeof(PostProcessorResult));

            // Unwrap executors do post_processor — input tipado (PostProcessorResult), saída raw
            // (não-JSON-encapsulada) pro consumidor downstream (agente boleta no loop ou consumidor terminal).
            registry.Register("unwrap_errors_to_text", (input, ct) =>
            {
                var r = System.Text.Json.JsonSerializer.Deserialize<PostProcessorResult>(input, postProcessorJsonOpts)
                        ?? new PostProcessorResult { HasErrors = true, OriginalOutput = input };
                return Task.FromResult(PostProcessorUnwrappers.FormatErrorsForAgent(r));
            });
            registry.RegisterSchema("unwrap_errors_to_text", typeof(PostProcessorResult), typeof(string));

            registry.Register("unwrap_post_processor_output", (input, ct) =>
            {
                var r = System.Text.Json.JsonSerializer.Deserialize<PostProcessorResult>(input, postProcessorJsonOpts);
                return Task.FromResult(r is null ? input : PostProcessorUnwrappers.ExtractValidatedOutput(r));
            });
            registry.RegisterSchema("unwrap_post_processor_output", typeof(PostProcessorResult), typeof(string));

            // router_fallback foi removido — workflows agora roteiam o case
            // `$.intent == "out_of_scope"` direto pro agente `fallback-atendimento`
            // (persona real, resposta natural). Migration 004 reescreve os
            // Switches existentes.

            // revisao_classificador — wrappa output em texto livre do agente revisor-analise-ativo.
            registry.Register<string, EfsAiHub.Core.Agents.Trading.RevisaoResultado>(
                "revisao_classificador",
                (input, ct) => RevisaoClassificador.ClassifyAsync(input, ct));

            // unwrap_aprovacao_to_ativo — extrai AprovadoPayload pro save_ativo_exec consumir.
            registry.Register<EfsAiHub.Core.Agents.Trading.RevisaoResultado, EfsAiHub.Core.Agents.Trading.Ativo>(
                "unwrap_aprovacao_to_ativo",
                (r, ct) =>
                {
                    if (r.AprovadoPayload is null)
                        throw new InvalidOperationException(
                            "unwrap_aprovacao_to_ativo: AprovadoPayload é null. " +
                            "Switch deveria rotear para esta branch apenas quando Status=APROVADO.");
                    return Task.FromResult(r.AprovadoPayload);
                });

            // unwrap_reprovacao_to_feedback — gera texto de feedback pro escritor refazer.
            registry.Register("unwrap_reprovacao_to_feedback", (input, ct) =>
            {
                var r = System.Text.Json.JsonSerializer.Deserialize<EfsAiHub.Core.Agents.Trading.RevisaoResultado>(
                    input, postProcessorJsonOpts) ?? new EfsAiHub.Core.Agents.Trading.RevisaoResultado { Status = "REPROVADO" };
                return Task.FromResult(RevisaoClassificador.FormatReprovacaoForEscritor(r));
            });
            registry.RegisterSchema(
                "unwrap_reprovacao_to_feedback",
                typeof(EfsAiHub.Core.Agents.Trading.RevisaoResultado),
                typeof(string));

            return registry;
        });

        return services;
    }

    // ── Npgsql Pools ────────────────────────────────────────────────────────────
    public static IServiceCollection AddNpgsqlPools(
        this IServiceCollection services, IConfiguration configuration, string pgConnectionString)
    {
        services.AddDbContextFactory<AgentFwDbContext>(o => o.UseNpgsql(pgConnectionString));

        // "general" — Chat Path + writes do hot path
        var generalMinPool = configuration.GetValue<int>("Npgsql:GeneralMinPoolSize", 10);
        var generalMaxPool = configuration.GetValue<int>("Npgsql:GeneralMaxPoolSize", 100);
        var generalConnectionString = new NpgsqlConnectionStringBuilder(pgConnectionString)
        { MinPoolSize = generalMinPool, MaxPoolSize = generalMaxPool }.ConnectionString;
        var generalDataSource = new NpgsqlDataSourceBuilder(generalConnectionString).Build();
        services.AddKeyedSingleton<NpgsqlDataSource>("general", generalDataSource);

        // "sse" — conexões LISTEN de longa duração
        var ssePoolSize = configuration.GetValue<int>("Npgsql:SseMaxPoolSize", 50);
        var sseConnectionString = new NpgsqlConnectionStringBuilder(pgConnectionString)
        { MaxPoolSize = ssePoolSize }.ConnectionString;
        var sseDataSource = new NpgsqlDataSourceBuilder(sseConnectionString).Build();
        services.AddKeyedSingleton<NpgsqlDataSource>("sse", sseDataSource);

        // "reporting" — leituras administrativas / analytics
        var reportingPoolSize = configuration.GetValue<int>("Npgsql:ReportingMaxPoolSize", 20);
        var reportingBaseConnectionString =
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("PostgresReporting"))
                ? pgConnectionString
                : configuration.GetConnectionString("PostgresReporting")!;
        var reportingConnectionString = new NpgsqlConnectionStringBuilder(reportingBaseConnectionString)
        { MaxPoolSize = reportingPoolSize }.ConnectionString;
        var reportingDataSource = new NpgsqlDataSourceBuilder(reportingConnectionString).Build();
        services.AddKeyedSingleton<NpgsqlDataSource>("reporting", reportingDataSource);

        // Registro sem chave para compatibilidade
        services.AddSingleton(generalDataSource);

        return services;
    }

    // ── Redis ───────────────────────────────────────────────────────────────────
    public static IConnectionMultiplexer? AddEfsRedis(
        this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
        var redisKeyPrefix = configuration.GetValue<string>("Redis:KeyPrefix") ?? "efs-ai-hub:";

        IConnectionMultiplexer? redisMultiplexer = null;
        try { redisMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString); }
        catch { /* Redis indisponível — Data Protection usará apenas armazenamento efêmero */ }

        if (redisMultiplexer is not null)
            services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
        else
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                throw new InvalidOperationException("Redis não está disponível neste ambiente."));

        services.AddSingleton<IEfsRedisCache>(sp =>
            new EfsRedisCache(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                redisKeyPrefix,
                sp.GetRequiredService<ILogger<EfsRedisCache>>()));
        services.AddSingleton<EfsAiHub.Core.Abstractions.Execution.IDistributedSlotCounter, RedisSlotCounter>();

        // Data Protection
        var dpBuilder = services.AddDataProtection()
            .SetApplicationName("EfsAiHub")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(730));
        if (redisMultiplexer is not null)
            dpBuilder.PersistKeysToStackExchangeRedis(redisMultiplexer, "DataProtection:Keys");

        return redisMultiplexer;
    }

    // ── Repositories ────────────────────────────────────────────────────────────
    public static IServiceCollection AddEfsRepositories(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowVersionRepository, PgWorkflowVersionRepository>();
        services.AddSingleton<IWorkflowDefinitionRepository, PgWorkflowDefinitionRepository>();
        services.AddSingleton<IAgentVersionRepository, PgAgentVersionRepository>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Execution.IModelPricingCache, EfsAiHub.Platform.Runtime.Execution.ModelPricingCache>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Execution.IDocumentIntelligencePricingCache, EfsAiHub.Platform.Runtime.Execution.DocumentIntelligencePricingCache>();
        services.AddSingleton<EfsAiHub.Core.Agents.Skills.ISkillVersionRepository, PgSkillVersionRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Skills.ISkillRepository, PgSkillRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Services.ISkillResolver, EfsAiHub.Core.Agents.Services.SkillResolver>();
        services.AddSingleton<EfsAiHub.Core.Agents.Services.IAgentTemplateService, EfsAiHub.Core.Agents.Services.AgentTemplateService>();
        services.AddScoped<EfsAiHub.Core.Agents.Services.IAgentDefinitionComposer, EfsAiHub.Core.Agents.Services.AgentDefinitionComposer>();
        services.AddSingleton<EfsAiHub.Core.Agents.Services.IAgentDefinitionDecomposer, EfsAiHub.Core.Agents.Services.AgentDefinitionDecomposer>();
        services.AddScoped<EfsAiHub.Core.Agents.Services.IAgentDependencyPropagator, EfsAiHub.Core.Agents.Services.AgentDependencyPropagator>();
        services.AddSingleton<EfsAiHub.Core.Agents.Responses.IBackgroundResponseRepository, PgBackgroundResponseRepository>();
        services.AddSingleton<IAgentDefinitionRepository, PgAgentDefinitionRepository>();
        services.AddScoped<IAgentDraftRepository, PgAgentDraftRepository>();
        services.AddSingleton<IAgentPromptRepository, PgAgentPromptRepository>();
        services.AddSingleton<IWorkflowExecutionRepository, PgWorkflowExecutionRepository>();
        services.AddSingleton<INodeExecutionRepository, PgNodeExecutionRepository>();
        services.AddSingleton<IConversationRepository, PgConversationRepository>();
        services.AddSingleton<IChatMessageRepository, PgChatMessageRepository>();
        services.AddSingleton<IMessageFeedbackRepository, PgMessageFeedbackRepository>();
        services.AddSingleton<IAtivoRepository, PgAtivoRepository>();
        services.AddSingleton<ILlmTokenUsageRepository, PgLlmTokenUsageRepository>();
        services.AddSingleton<IToolInvocationRepository, PgToolInvocationRepository>();
        services.AddSingleton<IModelPricingRepository, PgModelPricingRepository>();
        services.AddSingleton<IDocumentIntelligencePricingRepository, PgDocumentIntelligencePricingRepository>();
        services.AddSingleton<IDocumentIntelligenceUsageQueries, PgDocumentIntelligenceUsageQueries>();
        services.AddSingleton<IWorkflowEventRepository, PgWorkflowEventRepository>();
        services.AddSingleton<IExecutionAnalyticsRepository, PgExecutionAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IProjectAnalyticsRepository, PgProjectAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IToolAnalyticsRepository, PgToolAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IStandaloneJobAnalyticsRepository, PgStandaloneJobAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IWebhookDeliveryAnalyticsRepository, PgWebhookDeliveryAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IRouterDecisionAnalyticsRepository, PgRouterDecisionAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IFeedbackAnalyticsRepository, PgFeedbackAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IAdminAuditLogger, PgAdminAuditLogRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.McpServers.IMcpServerRepository, PgMcpServerRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Users.IUserDirectory, PgUserDirectory>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Users.IUserMembershipService, PgUserMembershipService>();
        services.AddSingleton<EfsAiHub.Core.Agents.DocumentIntelligence.IDocumentExtractionRepository, PgDocumentExtractionRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.DocumentIntelligence.IDocumentIntelligenceService, EfsAiHub.Platform.Runtime.Services.DocumentIntelligenceService>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Executors.DocumentIntelligenceFunctions>();

        // Evaluation subsystem repositories (ADR 0015)
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluationTestSetRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluationTestSetRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluationTestSetVersionRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluationTestSetVersionRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluationTestCaseRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluationTestCaseRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluatorConfigRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluatorConfigRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluatorConfigVersionRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluatorConfigVersionRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluationRunRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluationRunRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.Evaluation.IEvaluationResultRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgEvaluationResultRepository>();

        return services;
    }

    // ── Application Services ────────────────────────────────────────────────────
    public static IServiceCollection AddEfsApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<EfsAiHub.Core.Abstractions.Execution.IExecutionSlotRegistry, ChatExecutionRegistry>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IAgentDraftService, AgentDraftService>();
        services.AddScoped<IAgentApprovalService, AgentApprovalService>();
        services.AddScoped<WorkflowValidator>();
        services.AddScoped<EfsAiHub.Core.Orchestration.Validation.EdgeInvariantsValidator>();
        services.AddScoped<EfsAiHub.Core.Orchestration.Validation.WorkflowAgentInvariantsValidator>();
        services.AddScoped<EfsAiHub.Core.Orchestration.Validation.ChatValidationWarningCalculator>();
        services.AddScoped<EfsAiHub.Core.Abstractions.AgentSandbox.IAgentSandboxSessionRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgAgentSandboxSessionRepository>();
        services.AddScoped<EfsAiHub.Host.Api.AgentSandbox.AgentSandboxService>();
        // Bind primário em "AgentSandbox"; aceita também a seção legada
        // "ChatSandbox" pra que ambientes ainda não atualizados continuem
        // aplicando os mesmos TTLs/intervalos sem mudança de appsettings.
        services.Configure<EfsAiHub.Platform.Runtime.Options.AgentSandboxOptions>(
            configuration.GetSection("AgentSandbox"));
        services.PostConfigure<EfsAiHub.Platform.Runtime.Options.AgentSandboxOptions>(o =>
        {
            var legacy = configuration.GetSection("ChatSandbox");
            if (!legacy.Exists()) return;
            if (int.TryParse(legacy["SessionTtlDays"], out var ttl) && ttl > 0) o.SessionTtlDays = ttl;
            if (int.TryParse(legacy["CleanupIntervalSeconds"], out var iv)) o.CleanupIntervalSeconds = iv;
            if (int.TryParse(legacy["CleanupBatchSize"], out var bs) && bs > 0) o.CleanupBatchSize = bs;
        });
        services.AddScoped<EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter>(sp =>
            new EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter(
                sp.GetRequiredKeyedService<Npgsql.NpgsqlDataSource>("general"),
                sp.GetRequiredService<EfsAiHub.Core.Agents.IAgentDefinitionRepository>(),
                sp.GetRequiredService<EfsAiHub.Core.Orchestration.Interfaces.ICodeExecutorRegistry>(),
                sp.GetRequiredService<ILogger<EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter>>()));
        services.AddScoped<IExecutionDetailReader, ExecutionDetailAssembler>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IWorkflowDispatcher>(sp => (IWorkflowDispatcher)sp.GetRequiredService<IWorkflowService>());
        services.AddScoped<IWorkflowAgentVersionStatusService, WorkflowAgentVersionStatusService>();
        services.AddScoped<TokenCountUpdater>();
        services.AddScoped<ConversationService>();
        services.AddScoped<EfsAiHub.Core.Abstractions.Execution.IExecutionLifecycleObserver>(
            sp => sp.GetRequiredService<ConversationService>());
        services.AddScoped<IConversationLifecycle>(sp => sp.GetRequiredService<ConversationService>());
        services.AddScoped<IConversationFacade, ConversationFacade>();
        services.AddSingleton<ChatRateLimiter>();
        services.AddSingleton<ConversationLockManager>();
        services.AddHostApiIdentity();
        services.AddSingleton<TokenBatcher>();
        services.AddSingleton<TokenUsagePersistenceService>();
        services.AddSingleton<ITokenUsageSink>(sp => sp.GetRequiredService<TokenUsagePersistenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<TokenUsagePersistenceService>());
        services.AddSingleton<ToolInvocationPersistenceService>();
        services.AddSingleton<IToolInvocationSink>(sp => sp.GetRequiredService<ToolInvocationPersistenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<ToolInvocationPersistenceService>());

        // LLM Prompt Inspector — captura runtime-toggleable.
        services.AddScoped<EfsAiHub.Core.Agents.Capture.ILlmCaptureConfigRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgLlmCaptureConfigRepository>();
        services.AddScoped<EfsAiHub.Core.Agents.Capture.ILlmInvocationLogRepository,
            EfsAiHub.Infra.Persistence.Postgres.PgLlmInvocationLogRepository>();
        services.AddScoped<EfsAiHub.Platform.Runtime.Services.LlmCaptureConfigService>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Sanitization.ILlmPayloadSanitizer,
            EfsAiHub.Platform.Runtime.Sanitization.RegexLlmPayloadSanitizer>();
        services.AddSingleton<EfsAiHub.Host.Worker.Services.LlmInvocationLogPersistenceService>();
        services.AddSingleton<EfsAiHub.Core.Orchestration.Interfaces.ILlmInvocationLogSink>(
            sp => sp.GetRequiredService<EfsAiHub.Host.Worker.Services.LlmInvocationLogPersistenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<EfsAiHub.Host.Worker.Services.LlmInvocationLogPersistenceService>());
        services.AddSingleton<NodePersistenceService>();
        services.AddHostedService(sp => sp.GetRequiredService<NodePersistenceService>());
        services.AddSingleton<IHumanInteractionRepository, PgHumanInteractionRepository>();
        services.AddSingleton<HumanInteractionService>();
        services.AddSingleton<IHumanInteractionService>(sp => sp.GetRequiredService<HumanInteractionService>());
        services.AddSingleton<DiagramRenderingService>();

        // AG-UI Protocol
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.AgUiEventMapper>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Streaming.AgUiTokenChannel>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.AgUi.IAgUiTokenSink>(
            sp => sp.GetRequiredService<EfsAiHub.Host.Api.Chat.AgUi.Streaming.AgUiTokenChannel>());
        services.AddMemoryCache();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.State.IAgUiStateStore, EfsAiHub.Host.Api.Chat.AgUi.State.RedisAgUiStateStore>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.State.AgUiStateManager>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.State.AgUiSharedStateWriterAdapter>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.AgUi.IAgUiSharedStateWriter>(
            sp => sp.GetRequiredService<EfsAiHub.Host.Api.Chat.AgUi.State.AgUiSharedStateWriterAdapter>());
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.State.PredictiveStateEmitter>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Handlers.AgUiDisconnectRegistry>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Handlers.AgUiSseHandler>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Approval.AgUiApprovalMiddleware>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Handlers.AgUiCancellationHandler>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Handlers.AgUiFrontendToolHandler>();
        services.AddSingleton<EfsAiHub.Host.Api.Chat.AgUi.Handlers.AgUiReconnectionHandler>();

        // Multi-tenant context — ambos singleton com AsyncLocal pra fluir através de
        // escopos internos (IDbContextFactory, hosted services, background tasks).
        services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor,
            EfsAiHub.Host.Api.Middleware.TenantContextAccessor>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor,
            EfsAiHub.Host.Api.Middleware.ProjectContextAccessor>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Projects.IProjectRepository,
            PgProjectRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Projects.IModelCatalogRepository,
            PgModelCatalogRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Blocklist.IBlocklistCatalogRepository,
            PgBlocklistCatalogRepository>();

        // Blocklist Guardrail — engine + built-in patterns dinâmicos.
        // Engine é Singleton + IHostedService (subscreve NOTIFY no startup).
        services.AddSingleton<EfsAiHub.Platform.Runtime.Guards.BuiltIns.IBuiltInPatternHandler,
            EfsAiHub.Platform.Runtime.Guards.BuiltIns.InternalToolsPattern>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Guards.BlocklistEngine>();
        services.AddHostedService(sp =>
            sp.GetRequiredService<EfsAiHub.Platform.Runtime.Guards.BlocklistEngine>());

        // Sessions
        services.AddSingleton<IAgentSessionStore, PgAgentSessionStore>();
        services.AddScoped<AgentSessionService>();

        // Circuit Breaker
        services.Configure<CircuitBreakerOptions>(configuration.GetSection("CircuitBreaker"));
        services.AddSingleton<LlmCircuitBreaker>();

        // Feature flags do sharing cross-project. IOptionsMonitor permite
        // alterar runtime via reload do appsettings sem restart.
        services.Configure<EfsAiHub.Core.Abstractions.Sharing.SharingOptions>(
            configuration.GetSection(EfsAiHub.Core.Abstractions.Sharing.SharingOptions.SectionName));

        // Project Rate Limiting + Budget Guard
        services.AddSingleton<EfsAiHub.Platform.Runtime.Guards.ProjectRateLimiter>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Guards.ProjectBudgetGuard>();

        // Workflow execution
        services.AddScoped<ExecutionFailureWriter>();
        services.AddScoped<EfsAiHub.Host.Worker.Services.EventHandlers.AgentHandoffEventHandler>();
        services.AddScoped<WorkflowRunnerRepositories>();
        services.AddScoped<WorkflowRunnerCollaborators>();
        services.AddScoped<WorkflowRunnerService>();
        services.AddScoped<IWorkflowExecutor, WorkflowExecutor>();

        // Checkpointing
        services.AddSingleton<EfsAiHub.Infra.Persistence.Checkpointing.FrameworkCheckpointStoreAdapter>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Checkpointing.IEngineCheckpointAdapter,
            EfsAiHub.Platform.Runtime.Checkpointing.EngineCheckpointAdapter>();

        // Hosted services
        // WorkflowEngine options são lidas localmente para gating dos serviços opcionais.
        // Não chamamos services.Configure<...>() aqui porque quem chama este método
        // já pode ter registrado o options binder; pegar direto da IConfiguration evita
        // depender de ordem de registro.
        var engineOpts = configuration.GetSection(WorkflowEngineOptions.SectionName).Get<WorkflowEngineOptions>()
            ?? new WorkflowEngineOptions();

        services.AddHostedService<DatabaseBootstrapService>();
        // AgentVersionBackfillService removido (decisão 2026-05-29): rodar
        // recompose/upsert a cada startup era opaco (criava revision nova em
        // silêncio quando o composer divergia do snapshot) e custoso. Agentes
        // seedados via db/seeds.sql ficam sem agent_versions row até a primeira
        // edição via API/UI — fluxo aceitável porque seed acontece raramente
        // e via PR explícito.
        if (engineOpts.MultiNode)
            services.AddHostedService<CrossNodeCoordinator>();

        // Standalone Pools — workflows assíncronos com fila isolada (Redis slot
        // counter scope=standalone + lease em background_response_jobs).
        // Gateado pela feature flag StandalonePools:Enabled — quando false, o
        // dispatcher fica idle (não consome jobs, não bloqueia recursos).
        // O reaper sempre roda: idempotente e protege contra jobs órfãos mesmo
        // após desligar a feature.
        services.AddOptions<EfsAiHub.Platform.Runtime.Configuration.StandalonePoolsOptions>()
            .BindConfiguration(EfsAiHub.Platform.Runtime.Configuration.StandalonePoolsOptions.SectionName);
        services.AddOptions<EfsAiHub.Platform.Runtime.Configuration.IngestionApiOptions>()
            .BindConfiguration(EfsAiHub.Platform.Runtime.Configuration.IngestionApiOptions.SectionName);
        services.AddOptions<EfsAiHub.Platform.Runtime.Configuration.WebhookDeliveryOptions>()
            .BindConfiguration(EfsAiHub.Platform.Runtime.Configuration.WebhookDeliveryOptions.SectionName);

        // Webhook deliveries — repository pra entregas + worker que processa
        // pending (1 tentativa, sem retry).
        services.AddSingleton<EfsAiHub.Core.Agents.Responses.IWebhookDeliveryRepository,
            PgWebhookDeliveryRepository>();

        // Ingestion pipeline (URL → PDF/TXT/MD → DI → workflow).
        services.AddSingleton<EfsAiHub.Platform.Runtime.Ingestion.IngestionDownloader>();

        // Handlers de jobs standalone. Ordem importa: IngestionJobHandler antes
        // do default — primeiro que CanHandle ganha. WorkflowStandaloneJobHandler
        // aceita qualquer job, então é o fallback.
        services.AddSingleton<EfsAiHub.Host.Worker.Services.Handlers.IStandaloneJobHandler,
            EfsAiHub.Host.Worker.Services.Handlers.IngestionJobHandler>();
        services.AddSingleton<EfsAiHub.Host.Worker.Services.Handlers.IStandaloneJobHandler,
            EfsAiHub.Host.Worker.Services.Handlers.WorkflowStandaloneJobHandler>();

        services.AddHostedService<EfsAiHub.Host.Worker.Services.StandaloneJobDispatcherService>();
        services.AddHostedService<EfsAiHub.Host.Worker.Services.WebhookCallbackDeliveryService>();

        // Background Service Registry — propagamos as opções pra refletir o que foi
        // efetivamente registrado (intervalos reais + gating do CrossNodeCoordinator).
        services.AddBackgroundServiceRegistry(engineOpts);

        // Evaluation subsystem (ADR 0015)
        services.Configure<EfsAiHub.Platform.Runtime.Evaluation.EvaluationOptions>(
            configuration.GetSection(EfsAiHub.Platform.Runtime.Evaluation.EvaluationOptions.SectionName));
        // INVARIANTE: IAgentFactory resolve para AgentFactory concreta.
        // EvaluationRunnerService usa CreateBareAgentAsync (não exposto na
        // interface). Decorar IAgentFactory quebra este cast em runtime.
        services.AddScoped<EfsAiHub.Platform.Runtime.Factories.AgentFactory>(sp =>
            (EfsAiHub.Platform.Runtime.Factories.AgentFactory)sp.GetRequiredService<IAgentFactory>());
        services.AddSingleton<EfsAiHub.Platform.Runtime.Evaluation.EvaluatorFactory>();
        // FoundryJudgeClientFactory: Singleton para compartilhar cache (por
        // projectId, TTL 5min) entre runs/scopes.
        services.AddSingleton<EfsAiHub.Platform.Runtime.Evaluation.IFoundryJudgeClientFactory,
            EfsAiHub.Platform.Runtime.Evaluation.FoundryJudgeClientFactory>();
        services.AddScoped<EfsAiHub.Platform.Runtime.Evaluation.IEvaluationService,
            EfsAiHub.Platform.Runtime.Evaluation.EvaluationService>();
        services.AddScoped<EfsAiHub.Platform.Runtime.Evaluation.EvaluationAutoDeployService>();
        services.AddScoped<EfsAiHub.Host.Api.Services.Evaluation.IAgentDefinitionApplicationService,
            EfsAiHub.Host.Api.Services.Evaluation.AgentDefinitionApplicationService>();
        services.AddHostedService<EfsAiHub.Host.Worker.Services.EvaluationRunnerService>();

        return services;
    }

    // ── Background Service Registry ─────────────────────────────────────────────
    public static IServiceCollection AddBackgroundServiceRegistry(
        this IServiceCollection services,
        WorkflowEngineOptions? engineOpts = null)
    {
        var opts = engineOpts ?? new WorkflowEngineOptions();

        // Sink in-memory de heartbeats. Singleton — todos os hosted services injetam
        // o mesmo dicionário thread-safe e reportam Started/RecordSuccess/RecordError.
        // Per-pod: em multi-instance, cada pod tem o próprio sink. Endpoint admin
        // devolve só os heartbeats do pod que serviu o request.
        services.AddSingleton<EfsAiHub.Core.Abstractions.BackgroundServices.IBackgroundServiceHeartbeatSink,
            EfsAiHub.Platform.Runtime.Services.BackgroundServiceHeartbeatSink>();

        services.AddSingleton<EfsAiHub.Core.Abstractions.BackgroundServices.IBackgroundServiceRegistry>(_ =>
        {
            var registry = new EfsAiHub.Platform.Runtime.Services.BackgroundServiceRegistry();

            // ── Bootstrap (rodam uma vez no startup) ─────────────────────────
            registry.Register("DatabaseBootstrap",
                new() { Name = "DatabaseBootstrap",
                    Description = "Limpeza no startup de execuções órfãs deixadas por restart",
                    Lifecycle = "OneTime",
                    Category = BackgroundServiceCategory.Bootstrap,
                    ServiceType = typeof(DatabaseBootstrapService) });
            registry.Register("AdminPermissionsStartupValidator",
                new() { Name = "AdminPermissionsStartupValidator",
                    Description = "Valida config Admin:AdminPermissions no boot — warning quando vazia fora de Development.",
                    Lifecycle = "OneTime",
                    Category = BackgroundServiceCategory.Bootstrap,
                    ServiceType = typeof(EfsAiHub.Host.Api.Services.AdminPermissionsStartupValidator) });

            // ── Persistence (drenam Channel bounded e persistem em batch) ────
            registry.Register("TokenUsagePersistence",
                new() { Name = "TokenUsagePersistence",
                    Description = "Persiste consumo de tokens em lote (channel-driven, batch=10).",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Persistence,
                    ServiceType = typeof(TokenUsagePersistenceService) });
            registry.Register("ToolInvocationPersistence",
                new() { Name = "ToolInvocationPersistence",
                    Description = "Persiste invocações de tools em lote (channel-driven, batch=10).",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Persistence,
                    ServiceType = typeof(ToolInvocationPersistenceService) });
            registry.Register("LlmInvocationLogPersistence",
                new() { Name = "LlmInvocationLogPersistence",
                    Description = "Drena channel de captura de prompts LLM e persiste em llm_invocation_log (batch=50).",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Persistence,
                    ServiceType = typeof(LlmInvocationLogPersistenceService) });
            registry.Register("NodePersistence",
                new() { Name = "NodePersistence",
                    Description = "Persiste sequencialmente o estado dos nós de workflow + publica eventos.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Persistence,
                    ServiceType = typeof(NodePersistenceService) });

            // ── Dispatcher (filas de jobs e entrega de webhooks) ─────────────
            registry.Register("StandaloneJobDispatcher",
                new() { Name = "StandaloneJobDispatcher",
                    Description = "Consome jobs da fila standalone (background_response_jobs) e dispara workflows assíncronos. Gateado por StandalonePools:Enabled.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Dispatcher,
                    ServiceType = typeof(StandaloneJobDispatcherService) });
            registry.Register("WebhookCallbackDelivery",
                new() { Name = "WebhookCallbackDelivery",
                    Description = "Entrega webhooks de jobs standalone terminais (CallbackTarget). Uma tentativa por delivery — sem retry.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Dispatcher,
                    ServiceType = typeof(WebhookCallbackDeliveryService) });

            // ── Evaluation ───────────────────────────────────────────────────
            registry.Register("EvaluationRunner",
                new() { Name = "EvaluationRunner",
                    Description = "Processa EvaluationRun em Pending. Dequeue atômico + Parallel.ForEachAsync sobre cases.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Evaluation,
                    ServiceType = typeof(EvaluationRunnerService) });

            // ── Messaging (LISTEN/NOTIFY cross-pod) ──────────────────────────
            // CrossNodeCoordinator só aparece no registry se o hosted service foi registrado
            // (gated por WorkflowEngine:MultiNode) — evita confusão na UI em deploy single-node.
            if (opts.MultiNode)
                registry.Register("CrossNodeCoordinator",
                    new() { Name = "CrossNodeCoordinator",
                        Description = "Propaga cancelamentos e eventos HITL entre pods via LISTEN/NOTIFY.",
                        Lifecycle = "Continuous",
                        Category = BackgroundServiceCategory.Messaging,
                        ServiceType = typeof(CrossNodeCoordinator) });
            registry.Register("PgNotifyDispatcher",
                new() { Name = "PgNotifyDispatcher",
                    Description = "Dispatcher singleton que multiplexa LISTEN em wf_events, efs_cache_invalidate, blocklist_changed, eval_run_cancelled.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Messaging,
                    ServiceType = typeof(EfsAiHub.Infra.Messaging.PgNotifyDispatcher) });

            // ── Guards (hot-reload de configs de runtime) ────────────────────
            registry.Register("BlocklistEngine",
                new() { Name = "BlocklistEngine",
                    Description = "Resolve BlocklistMatcher efetivo por projeto. Hot-reload via NOTIFY 'blocklist_changed' + cache híbrido L1/L2.",
                    Lifecycle = "Continuous",
                    Category = BackgroundServiceCategory.Guards,
                    ServiceType = typeof(EfsAiHub.Platform.Runtime.Guards.BlocklistEngine) });

            return registry;
        });

        return services;
    }
}
