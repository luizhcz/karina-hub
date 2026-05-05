using EfsAiHub.Core.Abstractions.Blocklist;

namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>Configurações de um projeto. Serializado como JSONB.</summary>
public sealed record ProjectSettings
{
    /// <summary>Versão do schema — permite migração de settings antigos sem NullReference.</summary>
    public int SchemaVersion { get; init; } = 1;

    public string? DefaultProvider { get; init; }
    public string? DefaultModel { get; init; }
    public float? DefaultTemperature { get; init; }

    public int? MaxTokensPerDay { get; init; }
    public decimal? MaxCostUsdPerDay { get; init; }

    public int? MaxConcurrentExecutions { get; init; }
    public int? MaxRequestsPerMinute { get; init; }
    public int? MaxConversationsPerUser { get; init; }

    public bool HitlEnabled { get; init; } = true;
    public bool BackgroundResponsesEnabled { get; init; } = true;

    public int? MaxSandboxTokensPerDay { get; init; } = 50_000;

    /// <summary>
    /// Override do projeto sobre o catálogo de blocklist curado pelo DBA.
    /// Null preserva compat com projetos antigos — engine resolve com BlocklistSettings.Default.
    /// </summary>
    public BlocklistSettings? Blocklist { get; init; }

    public EvaluationProjectSettings? Evaluation { get; init; }

    public ProjectSettings Migrate()
    {
        return SchemaVersion switch
        {
            1 => this with { Blocklist = Blocklist ?? BlocklistSettings.Default },
            _ => this with { Blocklist = Blocklist ?? BlocklistSettings.Default }
        };
    }
}

public sealed record EvaluationProjectSettings
{
    public FoundryEvaluationSettings? Foundry { get; init; }

    /// <summary>
    /// Override do modelo usado como judge LLM em bindings kind=meai. Quando
    /// null ou Enabled=false, o runner reusa o modelo do próprio agente avaliado
    /// (legacy — barato mas com bias de self-evaluation). Banking/regulado deve
    /// setar pra um judge isolado.
    /// </summary>
    public MeaiEvaluationSettings? Meai { get; init; }
}

/// <summary>
/// Modelo dedicado pra MEAI evaluators atuarem como judge separado do modelo
/// do agente — elimina bias de self-evaluation. Shape espelha
/// <see cref="FoundryEvaluationSettings"/> mas o judge é um IChatClient
/// ChatCompletion genérico (não exclusivo Foundry).
/// </summary>
public sealed record MeaiEvaluationSettings
{
    /// <summary>Default false — fallback no modelo do agente.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>"OpenAI" | "AzureOpenAI" | "AzureFoundry".</summary>
    public string? Provider { get; init; }

    /// <summary>Nome do deployment no provider (ex.: "gpt-4o-judge").</summary>
    public string? DeploymentName { get; init; }

    /// <summary>
    /// Endpoint do Azure quando Provider é AzureOpenAI/AzureFoundry. Ignorado
    /// para OpenAI (usa endpoint default).
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Referência de secret (ex.: "secret://aws/efs-meai-judge-key"). Aceita
    /// literal API key se não começa com "secret://" (legacy — desencorajado).
    /// </summary>
    public string? ApiKeyRef { get; init; }
}

/// <summary>
/// Deployment Azure AI Foundry dedicado por tenant para uso como judge LLM.
/// Privacy boundary: cada tenant tem seu próprio endpoint, isolando PII de
/// outros tenants no caminho de eval com Safety/Quality MEAI evaluators.
/// </summary>
public sealed record FoundryEvaluationSettings
{
    /// <summary>Default false — bindings kind=Foundry rejeitam 400 quando Enabled=false.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Endpoint Azure OpenAI / Azure AI Foundry para chat completion (judge LLM).
    /// Usado por evaluators Quality (Relevance/Coherence/Groundedness/etc).
    /// </summary>
    public string? Endpoint { get; init; }

    public string? ModelDeployment { get; init; }

    /// <summary>
    /// Referência para resolver API key (ex.: "secret://efs-foundry-tenant-X").
    /// Aceita literal API key se não começa com "secret://".
    /// </summary>
    public string? ApiKeyRef { get; init; }

    /// <summary>
    /// Endpoint do Azure AI Foundry Project (não-Hub).
    /// Obrigatório só para evaluators Safety (Violence/Sexual/SelfHarm/HateAndUnfairness)
    /// porque chamam Azure AI Content Safety API, não chat completion.
    /// Auth via DefaultAzureCredential. Sem isso, bindings Safety são pulados com warning.
    /// </summary>
    public string? ProjectEndpoint { get; init; }
}
