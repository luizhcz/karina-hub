using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using EfsAiHub.Host.Api.Extensions;
using EfsAiHub.Host.Api.Identity;
using EfsAiHub.Infra.Messaging.Extensions;
using EfsAiHub.Infra.Persistence.CheckpointStore;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Secrets;
using EfsAiHub.Infra.Secrets.Configuration;
using EfsAiHub.Infra.Secrets.Health;
using EfsAiHub.Platform.Runtime.Interfaces;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Resolve refs em Secrets:Bootstrap contra AWS antes que qualquer outra config
// seja lida. Retorna o mapa original (config-key → secret://aws/...) que vai
// alimentar o preload runtime junto com refs de projects/agents do DB.
// No-op quando a seção está vazia (ex: dev sem AWS configurado).
var bootstrapMap = builder.Configuration.AddAwsSecretsBootstrap();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174", "http://localhost:3000"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins)
     .WithHeaders("Content-Type", "Authorization",
         "x-efs-account", "x-efs-user-profile-id", "x-efs-permissions",
         "x-tenant-id", "x-project-id", "x-workflow-id",
         "app_origin", "access_token")
     .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")));

// ── Configuração strongly-typed ──────────────────────────────────────────────
builder.Services.AddLlmProviders(builder.Configuration);
builder.Services.AddPersonaResolution(builder.Configuration);
// Registra o decorator de cache e o PromptComposer/SystemMessageBuilder usados pelo AgentFactory.
// CachedPersonaProvider decora HttpPersonaProvider (já registrado em AddPersonaResolution).
// IPersonaProvider pro consumidor final = CachedPersonaProvider.
builder.Services.AddSingleton<EfsAiHub.Platform.Runtime.Execution.CachedPersonaProvider>();
builder.Services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaProvider>(
    sp => sp.GetRequiredService<EfsAiHub.Platform.Runtime.Execution.CachedPersonaProvider>());
builder.Services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaPromptComposer,
    EfsAiHub.Platform.Runtime.Personalization.PersonaPromptComposer>();
builder.Services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaPromptTemplateRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgPersonaPromptTemplateRepository>();
builder.Services.AddSingleton<EfsAiHub.Core.Abstractions.Identity.Persona.IPersonaPromptExperimentRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgPersonaPromptExperimentRepository>();
builder.Services.AddSingleton<EfsAiHub.Platform.Runtime.Execution.IPersonaPromptTemplateCache,
    EfsAiHub.Platform.Runtime.Execution.PersonaPromptTemplateCache>();
builder.Services.AddSingleton<EfsAiHub.Platform.Runtime.Factories.ISystemMessageBuilder,
    EfsAiHub.Platform.Runtime.Factories.SystemMessageBuilder>();
builder.Services.AddScoped<EfsAiHub.Host.Api.Services.PersonaResolutionService>();
builder.Services.Configure<WorkflowEngineOptions>(
    builder.Configuration.GetSection(WorkflowEngineOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(
    builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<ChatRoutingOptions>(
    builder.Configuration.GetSection(ChatRoutingOptions.SectionName));
builder.Services.Configure<ChatRateLimitOptions>(
    builder.Configuration.GetSection(ChatRateLimitOptions.SectionName));
builder.Services.Configure<AdminOptions>(
    builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<UserProvisioningOptions>(
    builder.Configuration.GetSection(UserProvisioningOptions.SectionName));
builder.Services.Configure<EfsAiHub.Platform.Runtime.Options.DocumentIntelligenceOptions>(
    builder.Configuration.GetSection(EfsAiHub.Platform.Runtime.Options.DocumentIntelligenceOptions.SectionName));
builder.Services.Configure<EfsAiHub.Platform.Runtime.Configuration.GenericToolsOptions>(
    builder.Configuration.GetSection(EfsAiHub.Platform.Runtime.Configuration.GenericToolsOptions.SectionName));

// ── Azure Identity ────────────────────────────────────────────────────────────
// SP do Azure é resolvido lazy a partir de Azure:ServicePrincipal:* (populado
// via Secrets:Bootstrap → AWS Secrets Manager). App sobe sem SP cadastrado;
// o factory só lança quando algum SDK Azure de fato pede um token. Mensagem
// contextual aponta o que precisa ser cadastrado no AWS.
builder.Services.AddSingleton<TokenCredential, LazyAzureServicePrincipalCredential>();

// ── AWS Secrets Manager (boot only) ──────────────────────────────────────────
// Cliente AWS é Singleton mas só usado durante o preload no boot. Runtime
// consome do IRuntimeSecretStore (in-memory, sub-microssegundo). Restart é
// obrigatório pra picking up rotação ou novo project/agent com secret novo.
builder.Services.AddAwsSecretsManager(builder.Configuration, bootstrapMap);
builder.Services.AddSingleton<MutableRuntimeSecretStoreHost>();
builder.Services.AddSingleton<IRuntimeSecretStore>(
    sp => sp.GetRequiredService<MutableRuntimeSecretStoreHost>());

// Sources varridos no preload (projects + agents). Adicionar source novo aqui
// quando algum outro lugar do domínio armazenar 'secret://aws/...'.
builder.Services.AddScoped<ISecretReferenceSource,
    EfsAiHub.Host.Api.Identity.Secrets.ProjectSecretReferenceSource>();
builder.Services.AddScoped<ISecretReferenceSource,
    EfsAiHub.Host.Api.Identity.Secrets.AgentSecretReferenceSource>();

// ── CheckpointStore: InMemory (dev) ou Postgres (produção) ───────────────────
var engineOptions = builder.Configuration
    .GetSection(WorkflowEngineOptions.SectionName)
    .Get<WorkflowEngineOptions>() ?? new WorkflowEngineOptions();

if (engineOptions.CheckpointMode == "Postgres")
    builder.Services.AddSingleton<ICheckpointStore, PgCheckpointStore>();
else
    builder.Services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();

// ── Infra.Messaging (PgEventBus SSE backbone + PgCrossNodeBus coordination)
builder.Services.AddMessaging();
// IEventBuffer (in-memory 1 pod hoje; trocar por AddRedisStreamEventBuffer quando N pods)
builder.Services.AddEventBuffer(builder.Configuration);

// ── Extension Points ─────────────────────────────────────────────────────────
builder.Services.AddAgentMiddlewareRegistry();
builder.Services.AddFunctionToolRegistry();
builder.Services.AddCodeExecutorRegistry();

// ── HttpClients ──────────────────────────────────────────────────────────────
var efsBackendUrl = builder.Configuration["EfsBackend:BaseUrl"] ?? "http://localhost:5001";
builder.Services.AddHttpClient("efs-backend", c =>
{
    c.BaseAddress = new Uri(efsBackendUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("mermaid-ink", c =>
{
    c.BaseAddress = new Uri("https://mermaid.ink");
    c.Timeout = TimeSpan.FromSeconds(15);
});
// Timeout exclusivo via CancellationTokenSource no executor. Default do
// HttpClient (100s) cortaria antes do CTS quando MaxTimeoutSeconds > 100,
// então fixamos Infinite aqui pro executor manter controle total.
builder.Services.AddHttpClient("generic-tool-executor", c =>
{
    c.Timeout = Timeout.InfiniteTimeSpan;
});
// Mesmo padrão do executor: o tester controla o timeout via CTS (10s fixo).
builder.Services.AddHttpClient("generic-tool-tester", c =>
{
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<BoletaToolFunctions>();
builder.Services.AddScoped<EfsAiHub.Core.Agents.IGenericToolRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgGenericToolRepository>();
builder.Services.AddScoped<EfsAiHub.Core.Agents.IOperationalMemoryRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgOperationalMemoryRepository>();
// SchemaCache compartilhado entre executor (runtime) e tester (UI) — schemas
// parseados ficam cacheados in-process por hash do JSON. Singleton porque o
// cache é stateful por instância e cross-request é desejado.
builder.Services.AddSingleton<EfsAiHub.Platform.Runtime.Tools.Generic.SchemaCache>();
// SchemaNormalizer é stateless/determinístico — singleton. Consumido só no
// save-time pelo GenericToolService (PR 2 wira o invoke).
builder.Services.AddSingleton<
    EfsAiHub.Platform.Runtime.Tools.Generic.Schema.ISchemaNormalizer,
    EfsAiHub.Platform.Runtime.Tools.Generic.Schema.SchemaNormalizer>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Tools.Generic.GenericResponseProjector>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Tools.Generic.IGenericToolExecutor,
    EfsAiHub.Platform.Runtime.Tools.Generic.GenericToolExecutor>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Tools.Generic.IGenericToolTester,
    EfsAiHub.Platform.Runtime.Tools.Generic.GenericToolTester>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Interfaces.IGenericToolService,
    EfsAiHub.Platform.Runtime.Services.GenericToolService>();
builder.Services.AddScoped<EfsAiHub.Core.Agents.IRouterIntentRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgRouterIntentRepository>();
builder.Services.AddScoped<EfsAiHub.Core.Agents.IAgentRouterIntentLinkRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgAgentRouterIntentLinkRepository>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Interfaces.IRouterIntentService,
    EfsAiHub.Platform.Runtime.Services.RouterIntentService>();
builder.Services.AddScoped<EfsAiHub.Core.Agents.IPredefinedModelRepository,
    EfsAiHub.Infra.Persistence.Postgres.PgPredefinedModelRepository>();
builder.Services.AddScoped<EfsAiHub.Platform.Runtime.Interfaces.IPredefinedModelService,
    EfsAiHub.Platform.Runtime.Services.PredefinedModelService>();

// ── Factories (Agente e Workflow) ─────────────────────────────────────────────
builder.Services.AddScoped<IAgentFactory, AgentFactory>();
builder.Services.AddScoped<IWorkflowFactory, WorkflowFactory>();
builder.Services.AddSingleton<EfsAiHub.Core.Orchestration.Workflows.IEdgePredicateEvaluator,
    EfsAiHub.Platform.Runtime.Predicates.EdgePredicateEvaluator>();

// ── Infrastructure ──────────────────────────────────────────────────────────
var pgConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=efs_ai_hub;Username=efs_ai_hub;Password=CHANGE_ME";
builder.Services.AddNpgsqlPools(builder.Configuration, pgConnectionString);
builder.Services.AddEfsRedis(builder.Configuration);
builder.Services.AddEfsRepositories();
builder.Services.AddEfsApplicationServices(builder.Configuration);

// ── Health Checks ─────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddHealthChecks()
    .AddNpgSql(pgConnectionString, name: "postgres", tags: ["ready"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["ready"])
    .AddCheck<AwsSecretsHealthCheck>("aws-secrets", tags: ["ready"])
    // Reporta agents globais com owner project deletado (Degraded, não Unhealthy).
    .AddCheck<EfsAiHub.Host.Api.Health.SharedAgentsHealthCheck>("shared-agents", tags: ["ready", "sharing"])
    // Reporta AgentVersions com agent owner deletado (orphan pins).
    .AddCheck<EfsAiHub.Host.Api.Health.WorkflowAgentVersionHealthCheck>("agent-version-orphans", tags: ["ready", "sharing", "pinning"]);

// ── Controllers + JSON ──���─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // UnsafeRelaxedJsonEscaping mantém "ã", "ç", "õ" legíveis nos payloads
        // JSON (default escapa pra ã etc.). "Unsafe" refere-se apenas a
        // output direto em HTML sem escape — JSON puro em API é seguro.
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "EfsAiHub.Api API",
        Version = "v1",
        Description = "API para criação e orquestração de Agentes e Workflows sobre o Microsoft Agent Framework"
    });
    c.EnableAnnotations();
});

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
var otelOptions = builder.Configuration
    .GetSection(ObservabilityOptions.SectionName)
    .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(otelOptions.ServiceName))
    .WithTracing(t =>
    {
        t.AddSource(ActivitySources.WorkflowExecution)
         .AddSource(ActivitySources.AgentInvocation)
         .AddSource(ActivitySources.LlmCall)
         .AddSource(ActivitySources.ToolCall)
         .AddSource(ActivitySources.EventBus)
         .AddAspNetCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otelOptions.OtlpEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddMeter(MetricsRegistry.MeterName)
         .AddAspNetCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otelOptions.OtlpEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
    });

builder.Logging.AddOpenTelemetry(o =>
{
    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(otelOptions.ServiceName));
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.ParseStateValues = true;

    if (!string.IsNullOrWhiteSpace(otelOptions.OtlpEndpoint))
        o.AddOtlpExporter(e => e.Endpoint = new Uri(otelOptions.OtlpEndpoint));
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Preload eager dos secrets ANTES de qualquer caminho que possa precisar deles.
// Bloqueante por design — AWS offline ou IAM sem GetSecretValue derrubam o boot
// (fail-fast operacional). Identificadores individuais que falham viram warning
// e o caller recebe null em runtime (mensagem própria contextual).
await RuntimeSecretStoreActivator.PreloadAndRegisterAsync(app.Services);
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var store = app.Services.GetRequiredService<IRuntimeSecretStore>();
    startupLogger.LogInformation(
        "[SecretsStore] {Count} segredo(s) AWS pré-carregado(s) em memória. Runtime não bate mais no Secrets Manager.",
        store.LoadedCount);
}

app.RegisterAtivoExecutors();
app.RegisterRedemptionTools();
app.RegisterPixExecutors();
app.RegisterDocumentIntelligenceExecutor();

// Loga o modo de provisioning ativo — facilita debug em prod quando alguém
// pergunta "por que o user X que chama AG-UI não aparece em aihub.users?".
{
    var provisioningOptions = app.Services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<UserProvisioningOptions>>()
        .Value;
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation(
        "[UserProvisioning] SkipAnonymousRoutes={Enabled} SkipPathPrefixes=[{Prefixes}]",
        provisioningOptions.SkipAnonymousRoutes,
        string.Join(", ", provisioningOptions.SkipPathPrefixes));
}

// Smoke: gera os JSON Schemas de todos os code executors tipados no startup.
// Tipos com problemas no JsonSchemaExporter (generics abertos, polymorphism sem
// [JsonDerivedType], etc) são silenciosamente puladados pelo registry — aqui
// logamos quantos foram cobertos vs quantos tipados ficaram sem schema, pra
// detectar regressões antes do tráfego real chegar.
{
    var codeRegistry = app.Services.GetRequiredService<ICodeExecutorRegistry>();
    var typedNames = codeRegistry.GetTypeInfo().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var schemas = codeRegistry.GetSchemas();
    var missing = typedNames.Except(schemas.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    if (missing.Length > 0)
        startupLogger.LogWarning(
            "[CodeExecutorSchemas] {Total} executores tipados, {Missing} sem schema gerado: {Names}",
            typedNames.Count, missing.Length, string.Join(", ", missing));
    else
        startupLogger.LogInformation(
            "[CodeExecutorSchemas] {Total} executores tipados com schemas JSON gerados.",
            typedNames.Count);
}

// ── ConfirmBoleta — HITL simples (request_approval) via function tool ────────
EfsAiHub.Platform.Runtime.Tools.ConfirmBoletaFunction.Configure(
    app.Services.GetRequiredService<EfsAiHub.Platform.Runtime.Services.IHumanInteractionService>(),
    app.Services.GetRequiredService<EfsAiHub.Core.Orchestration.Workflows.IWorkflowEventBus>(),
    app.Services.GetRequiredService<IFunctionToolRegistry>());

// Fase 6 — loga os fingerprints das function tools registradas no startup (auditoria).
{
    var registry = app.Services.GetRequiredService<IFunctionToolRegistry>();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    foreach (var (name, _) in registry.GetAll())
    {
        var fp = registry.GetLatestFingerprint(name);
        startupLogger.LogInformation("[ToolFingerprint] {Tool} = {Fingerprint}", name,
            fp is null ? "<none>" : fp[..Math.Min(16, fp.Length)]);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "EfsAiHub.Api v1"));
}

// Dev portal: ativo em Development ou quando DevPortal__Enabled=true
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("DevPortal:Enabled"))
{
    app.MapGet("/dev", async (HttpContext ctx) =>
    {
        var asm = typeof(Program).Assembly;
        const string resourceName = "EfsAiHub.Host.Api.DevPortal.devportal.html";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await stream.CopyToAsync(ctx.Response.Body);
    }).ExcludeFromDescription();
}

app.UseEfsMiddlewarePipeline();
app.UseEfsHealthChecks();

app.Run();

public partial class Program { }
