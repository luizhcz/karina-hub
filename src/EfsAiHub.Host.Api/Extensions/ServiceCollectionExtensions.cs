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

            return registry;
        });

        return services;
    }

    // ── Function Tool Registry ──────────────────────────────────────────────────
    public static IServiceCollection AddFunctionToolRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IFunctionToolRegistry>(sp =>
        {
            var registry = new FunctionToolRegistry(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<FunctionToolRegistry>());

            registry.Register("search_web", AIFunctionFactory.Create(WebSearchFunctions.SearchWeb));

            var boleta = sp.GetRequiredService<BoletaToolFunctions>();
            registry.Register("search_asset", AIFunctionFactory.Create(boleta.SearchAsset));
            registry.Register("get_asset_position", AIFunctionFactory.Create(boleta.GetAssetPosition));
            registry.Register("SendOrder", AIFunctionFactory.Create(boleta.SendOrder));

            registry.Register("get_portfolio", AIFunctionFactory.Create(ApexHandoffFunctions.GetPortfolio));
            registry.Register("redeem_asset", AIFunctionFactory.Create(ApexHandoffFunctions.RedeemAsset));
            registry.Register("invest_asset", AIFunctionFactory.Create(ApexHandoffFunctions.InvestAsset));
            registry.Register("calculate_asset_redemption_tax", AIFunctionFactory.Create(ApexHandoffFunctions.CalculateAssetRedemptionTax));

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

            // Router fallback — terminal alcançado quando router classifica target_agent="texto".
            registry.Register<RouterOutput, EfsAiHub.Core.Agents.Trading.OutputAtendimento>(
                "router_fallback",
                (r, ct) => RouterFallback.WrapAsync(r, ct));

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
        services.AddSingleton<EfsAiHub.Core.Agents.Responses.IBackgroundResponseRepository, PgBackgroundResponseRepository>();
        services.AddSingleton<IAgentDefinitionRepository, PgAgentDefinitionRepository>();
        services.AddSingleton<IAgentPromptRepository, PgAgentPromptRepository>();
        services.AddSingleton<IWorkflowExecutionRepository, PgWorkflowExecutionRepository>();
        services.AddSingleton<INodeExecutionRepository, PgNodeExecutionRepository>();
        services.AddSingleton<IConversationRepository, PgConversationRepository>();
        services.AddSingleton<IChatMessageRepository, PgChatMessageRepository>();
        services.AddSingleton<IAtivoRepository, PgAtivoRepository>();
        services.AddSingleton<ILlmTokenUsageRepository, PgLlmTokenUsageRepository>();
        services.AddSingleton<IToolInvocationRepository, PgToolInvocationRepository>();
        services.AddSingleton<IModelPricingRepository, PgModelPricingRepository>();
        services.AddSingleton<IDocumentIntelligencePricingRepository, PgDocumentIntelligencePricingRepository>();
        services.AddSingleton<IDocumentIntelligenceUsageQueries, PgDocumentIntelligenceUsageQueries>();
        services.AddSingleton<IWorkflowEventRepository, PgWorkflowEventRepository>();
        services.AddSingleton<IExecutionAnalyticsRepository, PgExecutionAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Observability.IAdminAuditLogger, PgAdminAuditLogRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.McpServers.IMcpServerRepository, PgMcpServerRepository>();
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
        services.AddScoped<WorkflowValidator>();
        services.AddScoped<EfsAiHub.Core.Orchestration.Validation.EdgeInvariantsValidator>();
        services.AddScoped<EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter>(sp =>
            new EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter(
                sp.GetRequiredKeyedService<Npgsql.NpgsqlDataSource>("general"),
                sp.GetRequiredService<EfsAiHub.Core.Agents.IAgentDefinitionRepository>(),
                sp.GetRequiredService<EfsAiHub.Core.Orchestration.Interfaces.ICodeExecutorRegistry>(),
                sp.GetRequiredService<ILogger<EfsAiHub.Platform.Runtime.Migration.EdgeMigrationReporter>>()));
        services.AddScoped<IExecutionDetailReader, ExecutionDetailAssembler>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IWorkflowDispatcher>(sp => (IWorkflowDispatcher)sp.GetRequiredService<IWorkflowService>());
        services.AddScoped<TokenCountUpdater>();
        services.AddScoped<ConversationService>();
        services.AddScoped<EfsAiHub.Core.Abstractions.Execution.IExecutionLifecycleObserver>(
            sp => sp.GetRequiredService<ConversationService>());
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
        services.AddHostedService<EfsAiHub.Host.Api.Chat.AgUi.Streaming.AgUiTokenChannelCleanupService>();
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

        // Phase 3 — Feature flags do épico multi-projeto. IOptionsMonitor permite
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
        services.AddHostedService<AgentSessionCleanupService>();
        services.AddHostedService<LlmCostRefreshService>();
        services.AddHostedService<AuditRetentionService>();
        if (engineOpts.MultiNode)
            services.AddHostedService<CrossNodeCoordinator>();
        services.AddHostedService<StuckExecutionRecoveryService>();
        // HitlRecoveryService DEVE ser registrado por último
        services.AddHostedService<HitlRecoveryService>();

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
        services.AddScoped<EfsAiHub.Host.Api.Services.Evaluation.IAgentDefinitionApplicationService,
            EfsAiHub.Host.Api.Services.Evaluation.AgentDefinitionApplicationService>();
        services.AddHostedService<EfsAiHub.Host.Worker.Services.EvaluationRunnerService>();
        services.AddHostedService<EfsAiHub.Host.Worker.Services.EvaluationReaperService>();

        return services;
    }

    // ── Background Service Registry ─────────────────────────────────────────────
    public static IServiceCollection AddBackgroundServiceRegistry(
        this IServiceCollection services,
        WorkflowEngineOptions? engineOpts = null)
    {
        var opts = engineOpts ?? new WorkflowEngineOptions();

        services.AddSingleton<EfsAiHub.Core.Abstractions.BackgroundServices.IBackgroundServiceRegistry>(_ =>
        {
            var registry = new EfsAiHub.Platform.Runtime.Services.BackgroundServiceRegistry();

            registry.Register("DatabaseBootstrap", new() { Name = "DatabaseBootstrap", Description = "Limpeza no startup de execuções órfãs deixadas por restart", Lifecycle = "OneTime", ServiceType = typeof(DatabaseBootstrapService) });
            registry.Register("AgentSessionCleanup", new() { Name = "AgentSessionCleanup", Description = "Remove sessões de agente expiradas pelo TTL", Lifecycle = "Continuous", Interval = TimeSpan.FromHours(6), ServiceType = typeof(AgentSessionCleanupService) });
            registry.Register("AuditRetention", new() { Name = "AuditRetention", Description = "Descarta partições antigas das tabelas de auditoria", Lifecycle = "Continuous", Interval = TimeSpan.FromHours(24), ServiceType = typeof(AuditRetentionService) });
            // CrossNodeCoordinator só aparece no registry se o hosted service foi registrado
            // (gated por WorkflowEngine:MultiNode) — evita confusão na UI em deploy single-node.
            if (opts.MultiNode)
                registry.Register("CrossNodeCoordinator", new() { Name = "CrossNodeCoordinator", Description = "Propaga cancelamentos e eventos HITL entre pods via LISTEN/NOTIFY", Lifecycle = "Continuous", ServiceType = typeof(CrossNodeCoordinator) });
            registry.Register("HitlRecovery", new() { Name = "HitlRecovery", Description = "Retoma execuções HITL pausadas após restart ou timeout", Lifecycle = "Continuous", Interval = TimeSpan.FromSeconds(Math.Max(1, opts.HitlRecoveryIntervalSeconds)), ServiceType = typeof(HitlRecoveryService) });
            registry.Register("StuckExecutionRecovery", new() { Name = "StuckExecutionRecovery", Description = "Marca como Failed execuções Running paradas há mais que o timeout configurado", Lifecycle = "Continuous", Interval = TimeSpan.FromSeconds(Math.Max(1, opts.StuckExecutionRecoveryIntervalSeconds)), ServiceType = typeof(StuckExecutionRecoveryService) });
            registry.Register("NodePersistence", new() { Name = "NodePersistence", Description = "Persiste sequencialmente o estado dos nós de workflow", Lifecycle = "Continuous", ServiceType = typeof(NodePersistenceService) });
            registry.Register("TokenUsagePersistence", new() { Name = "TokenUsagePersistence", Description = "Persiste consumo de tokens em lote", Lifecycle = "Continuous", ServiceType = typeof(TokenUsagePersistenceService) });
            registry.Register("ToolInvocationPersistence", new() { Name = "ToolInvocationPersistence", Description = "Persiste invocações de tools em lote", Lifecycle = "Continuous", ServiceType = typeof(ToolInvocationPersistenceService) });
            registry.Register("LlmCostRefresh", new() { Name = "LlmCostRefresh", Description = "Atualiza as views materializadas de custo de LLM", Lifecycle = "Continuous", Interval = TimeSpan.FromMinutes(Math.Max(1, opts.LlmCostRefreshIntervalMinutes)), ServiceType = typeof(LlmCostRefreshService) });
            registry.Register("AgUiTokenChannelCleanup", new() { Name = "AgUiTokenChannelCleanup", Description = "Remove canais SSE inativos do streaming AG-UI", Lifecycle = "Continuous", Interval = TimeSpan.FromMinutes(5), ServiceType = typeof(EfsAiHub.Host.Api.Chat.AgUi.Streaming.AgUiTokenChannelCleanupService) });

            return registry;
        });

        return services;
    }
}
