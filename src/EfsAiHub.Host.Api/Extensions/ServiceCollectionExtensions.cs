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
                description: "Controla o acesso a operações sensíveis de conta. Pode bloquear chamadas (ClientLocked) ou apenas logar (AssessorLogOnly).",
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
                            new MiddlewareSettingOption { Value = "AssessorLogOnly", Label = "Assessor Log Only" },
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
            registry.Register("buscar_ativo", AIFunctionFactory.Create(boleta.BuscarAtivo));
            registry.Register("ObterPosicaoCliente", AIFunctionFactory.Create(boleta.ObterPosicaoCliente));
            registry.Register("SendOrder", AIFunctionFactory.Create(boleta.SendOrder));

            registry.Register("ConsultarCarteira", AIFunctionFactory.Create(ApexHandoffFunctions.ConsultarCarteira));
            registry.Register("ExecutarResgate", AIFunctionFactory.Create(ApexHandoffFunctions.ExecutarResgate));
            registry.Register("ExecutarAplicacao", AIFunctionFactory.Create(ApexHandoffFunctions.ExecutarAplicacao));
            registry.Register("CalcularIrResgate", AIFunctionFactory.Create(ApexHandoffFunctions.CalcularIrResgate));

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
            var registry = new CodeExecutorRegistry();

            registry.Register("search_single", (input, ct) => WebSearchBatchFunctions.SearchSingle(input, ct));

            registry.Register("atendimento_pre_processor", (input, ct) => AtendimentoPreProcessor.EnrichInput(input, ct));
            registry.Register("atendimento_post_processor", (input, ct) => AtendimentoPostProcessor.ValidateAndEnrich(input, ct));

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
        services.AddSingleton<IWorkflowEventRepository, PgWorkflowEventRepository>();
        services.AddSingleton<IExecutionAnalyticsRepository, PgExecutionAnalyticsRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.DocumentIntelligence.IDocumentExtractionRepository, PgDocumentExtractionRepository>();
        services.AddSingleton<EfsAiHub.Core.Agents.DocumentIntelligence.IDocumentIntelligenceService, EfsAiHub.Platform.Runtime.Services.DocumentIntelligenceService>();
        services.AddSingleton<EfsAiHub.Platform.Runtime.Functions.DocumentIntelligenceFunctions>();

        return services;
    }

    // ── Application Services ────────────────────────────────────────────────────
    public static IServiceCollection AddEfsApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<EfsAiHub.Core.Abstractions.Execution.IExecutionSlotRegistry, ChatExecutionRegistry>();
        services.AddHttpClient("McpHealthCheck");
        services.AddSingleton<IMcpHealthChecker, McpHealthChecker>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<WorkflowValidator>();
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

        // Multi-tenant context
        services.AddScoped<EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor,
            EfsAiHub.Host.Api.Middleware.TenantContextAccessor>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor,
            EfsAiHub.Host.Api.Middleware.ProjectContextAccessor>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Projects.IProjectRepository,
            PgProjectRepository>();
        services.AddSingleton<EfsAiHub.Core.Abstractions.Projects.IModelCatalogRepository,
            PgModelCatalogRepository>();

        // Sessions
        services.AddSingleton<IAgentSessionStore, PgAgentSessionStore>();
        services.AddScoped<AgentSessionService>();

        // Circuit Breaker
        services.Configure<CircuitBreakerOptions>(configuration.GetSection("CircuitBreaker"));
        services.AddSingleton<LlmCircuitBreaker>();

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
        services.AddHostedService<DatabaseBootstrapService>();
        services.AddHostedService<AgentSessionCleanupService>();
        services.AddHostedService<LlmCostRefreshService>();
        services.AddHostedService<AuditRetentionService>();
        services.AddHostedService<CrossNodeCoordinator>();
        // HitlRecoveryService DEVE ser registrado por último
        services.AddHostedService<HitlRecoveryService>();

        // Background Service Registry
        services.AddBackgroundServiceRegistry();

        return services;
    }

    // ── Background Service Registry ─────────────────────────────────────────────
    public static IServiceCollection AddBackgroundServiceRegistry(this IServiceCollection services)
    {
        services.AddSingleton<EfsAiHub.Core.Abstractions.BackgroundServices.IBackgroundServiceRegistry>(_ =>
        {
            var registry = new EfsAiHub.Platform.Runtime.Services.BackgroundServiceRegistry();

            registry.Register("DatabaseBootstrap", new() { Name = "DatabaseBootstrap", Description = "Startup cleanup de execuções órfãs", Lifecycle = "OneTime", ServiceType = typeof(DatabaseBootstrapService) });
            registry.Register("AgentSessionCleanup", new() { Name = "AgentSessionCleanup", Description = "TTL cleanup de sessions expiradas", Lifecycle = "Continuous", Interval = TimeSpan.FromHours(6), ServiceType = typeof(AgentSessionCleanupService) });
            registry.Register("AuditRetention", new() { Name = "AuditRetention", Description = "Drop de partições expiradas", Lifecycle = "Continuous", Interval = TimeSpan.FromHours(24), ServiceType = typeof(AuditRetentionService) });
            registry.Register("CrossNodeCoordinator", new() { Name = "CrossNodeCoordinator", Description = "LISTEN/NOTIFY cancel + HITL cross-pod", Lifecycle = "Continuous", ServiceType = typeof(CrossNodeCoordinator) });
            registry.Register("HitlRecovery", new() { Name = "HitlRecovery", Description = "Resume de execuções HITL pausadas", Lifecycle = "Continuous", Interval = TimeSpan.FromSeconds(30), ServiceType = typeof(HitlRecoveryService) });
            registry.Register("NodePersistence", new() { Name = "NodePersistence", Description = "Persistência sequencial de estado de nós", Lifecycle = "Continuous", ServiceType = typeof(NodePersistenceService) });
            registry.Register("TokenUsagePersistence", new() { Name = "TokenUsagePersistence", Description = "Batch persist token usage", Lifecycle = "Continuous", ServiceType = typeof(TokenUsagePersistenceService) });
            registry.Register("ToolInvocationPersistence", new() { Name = "ToolInvocationPersistence", Description = "Batch persist tool invocations", Lifecycle = "Continuous", ServiceType = typeof(ToolInvocationPersistenceService) });
            registry.Register("LlmCostRefresh", new() { Name = "LlmCostRefresh", Description = "Refresh materialized views analytics", Lifecycle = "Continuous", Interval = TimeSpan.FromMinutes(5), ServiceType = typeof(LlmCostRefreshService) });
            registry.Register("AgUiTokenChannelCleanup", new() { Name = "AgUiTokenChannelCleanup", Description = "Cleanup SSE channels stale", Lifecycle = "Continuous", Interval = TimeSpan.FromMinutes(5), ServiceType = typeof(EfsAiHub.Host.Api.Chat.AgUi.Streaming.AgUiTokenChannelCleanupService) });

            return registry;
        });

        return services;
    }
}
