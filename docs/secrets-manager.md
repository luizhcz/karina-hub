# Secrets Manager — manual operacional

Manual de configuração das **secrets** que o EfsAiHub espera no provedor de secret manager (AWS Secrets Manager). Cobre o que precisa existir pro app subir, naming convention, permissões IAM, override em dev local e troubleshooting.

---

## 1. Visão geral

O backend tem **dois caminhos** que dependem do secret manager. Os dois usam a mesma fonte (AWS Secrets Manager) mas em momentos diferentes do ciclo de vida.

| Caminho | Quando | Falha = | Implementação |
|---|---|---|---|
| **Bootstrap** | Startup (síncrono, antes do `Run()`) | App não sobe (fail-fast) | [`AwsSecretsBootstrapExtensions.AddAwsSecretsBootstrap`](../src/EfsAiHub.Infra.Secrets/Configuration/AwsSecretsBootstrapExtensions.cs) |
| **Runtime** | Sob demanda (async, durante uma request) | A operação específica falha | [`AwsSecretsManagerResolver`](../src/EfsAiHub.Infra.Secrets/AwsSecretsManagerResolver.cs) via `ISecretResolver` |

**Bootstrap** resolve secrets globais (chaves de provider LLM compartilhadas pelo tenant) e injeta os valores em `IConfiguration` antes de qualquer DI. **Runtime** resolve secrets per-projeto/agente sob demanda quando uma execução precisa de uma credencial específica (ex.: agente com Provider.ApiKey próprio).

---

## 2. Naming convention

Toda referência segue o esquema URI:

```
secret://aws/<identifier>
```

Onde `<identifier>` é o nome (ou ARN) do secret no AWS Secrets Manager.

### Bootstrap (globais)

Secrets que existem **uma vez por ambiente** e são compartilhadas:

| Secret AWS | Conteúdo (string) | Mapeia pra config | Usado em |
|---|---|---|---|
| `efs-ai-hub/openai-default` | `sk-...` | `OpenAI:ApiKey` | OpenAI direto (provider `OpenAI`) |
| `efs-ai-hub/azureai-default` | chave do recurso Azure AI Foundry | `AzureAI:ApiKey` | Azure Foundry (Chat Completions e Responses API) |
| `efs-ai-hub/document-intelligence` | chave do recurso Azure DI | `DocumentIntelligence:ApiKey` | Document Intelligence |
| `efs-ai-hub/azure-sp-tenant-id` | GUID | `Azure:ServicePrincipal:TenantId` | Service Principal (Foundry) |
| `efs-ai-hub/azure-sp-client-id` | GUID | `Azure:ServicePrincipal:ClientId` | Service Principal |
| `efs-ai-hub/azure-sp-client-secret` | string | `Azure:ServicePrincipal:ClientSecret` | Service Principal |
| `efs-ai-hub/postgres` | connection string completa OU JSON `{"host":...,"username":...,"password":...,"port":5432}` | `ConnectionStrings:Postgres` | EF Core / Npgsql |

> **Observação importante**: o conteúdo do secret é **string única** que o Bootstrap injeta direto em `IConfiguration[<chave>]`. Não precisa estruturar o secret como JSON aninhado — o mapping pra config keys é feito pelo `Secrets:Bootstrap` no `appsettings`, não pelo conteúdo do secret.

### Runtime (per-projeto / per-agente)

Secrets específicas de um projeto ou agente (ex.: agente que chama uma OpenAI corporativa diferente da global). Resolvidas sob demanda via `ISecretResolver` + `SecretContext`. Convenções de nome:

| Escopo | Padrão de nome AWS | Exemplo |
|---|---|---|
| Global | `efs-ai-hub/<provider>-<label>` | `efs-ai-hub/openai-default` |
| Por projeto | `efs-ai-hub/projects/<projectId>/<provider>` | `efs-ai-hub/projects/6d3f9dbc.../openai` |
| Por agente | `efs-ai-hub/projects/<projectId>/agents/<agentId>` | `efs-ai-hub/projects/6d3f9dbc.../agents/agente-coletor-boleta` |
| Foundry (resource auth) | `efs-ai-hub/projects/<projectId>/foundry` | `efs-ai-hub/projects/6d3f9dbc.../foundry` |

O agente referencia o secret via `Provider.ApiKey = "secret://aws/<id>"` — o resolver troca pelo valor real no momento da execução.

---

## 3. Configuração no `appsettings`

A seção `Secrets:Bootstrap` lista as referências que o **Bootstrap** vai resolver no startup. Exemplo (`appsettings.Production.json`):

```json
{
  "Secrets": {
    "Aws": {
      "Region": "us-east-1",
      "RetryAttempts": 3,
      "L1TtlSeconds": 60,
      "L2TtlSeconds": 300,
      "L1MaxEntries": 500,
      "CacheKeyPrefix": "secret:",
      "HealthCheckCanaryReference": "secret://aws/efs-ai-hub/openai-default"
    },
    "Bootstrap": {
      "ConnectionStrings:Postgres":           "secret://aws/efs-ai-hub/postgres",
      "OpenAI:ApiKey":                        "secret://aws/efs-ai-hub/openai-default",
      "AzureAI:ApiKey":                       "secret://aws/efs-ai-hub/azureai-default",
      "DocumentIntelligence:ApiKey":          "secret://aws/efs-ai-hub/document-intelligence",
      "Azure:ServicePrincipal:TenantId":      "secret://aws/efs-ai-hub/azure-sp-tenant-id",
      "Azure:ServicePrincipal:ClientId":      "secret://aws/efs-ai-hub/azure-sp-client-id",
      "Azure:ServicePrincipal:ClientSecret":  "secret://aws/efs-ai-hub/azure-sp-client-secret"
    }
  }
}
```

**Cada chave em `Bootstrap`** vira uma entrada em `IConfiguration` com o **valor resolvido** do secret. Em runtime o app lê `_options.ApiKey` (de `IOptions<OpenAIOptions>`) sem nem perceber que veio de secret.

`HealthCheckCanaryReference` é uma referência usada pelo healthcheck `/health/secrets` pra validar conectividade com o AWS sem expor secret crítico — aponte pra um secret canário trivial.

### Cache (defaults razoáveis)

- `L1TtlSeconds` (60) — cache em memória do processo. Reduz latência em chamadas repetidas dentro de uma request.
- `L2TtlSeconds` (300) — cache distribuído (Redis). Compartilhado entre instâncias.
- `L1MaxEntries` (500) — limite de chaves na L1.

---

## 4. Permissões IAM

A IAM principal (role do pod/ECS task ou usuário em dev) precisa de:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ReadEfsAiHubSecrets",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret"
      ],
      "Resource": [
        "arn:aws:secretsmanager:us-east-1:<account-id>:secret:efs-ai-hub/*"
      ]
    }
  ]
}
```

**Restrição estrita**: limite o `Resource` ao prefixo `efs-ai-hub/*`. Não dê `secretsmanager:*` ou wildcard de `Resource`. Em ambientes multi-projeto, considere segregar por sub-prefixo (`efs-ai-hub/projects/<projectId>/*`) e atribuir IAM diferente por workload.

**Rotation**: o `AwsSecretsManagerResolver` lê o valor `AWSCURRENT` por padrão. Se ativar rotation com `AWSPREVIOUS` válido durante a janela de troca, no-op — só garanta que o cache TTL seja menor que o intervalo de rotation pra propagar a nova versão.

---

## 5. Dev local

Em dev, há **três opções** ordenadas por preferência:

### (a) AWS Secrets Manager real com perfil nomeado

Recomendado pra dev quando o time já tem credenciais AWS.

```bash
aws configure --profile efs-dev
# Access key, secret, region us-east-1
```

`.env`:
```
AWS_REGION=us-east-1
AWS_PROFILE=efs-dev
```

Backend resolve normalmente. Mantém paridade com staging/prod.

### (b) Override via `dotnet user-secrets` (sem AWS)

Pra quem não tem credencial AWS ou tá offline:

```bash
cd src/EfsAiHub.Host.Api
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "AzureAI:ApiKey" "..."
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;..."
```

`appsettings.Development.json` deve ter `Secrets:Bootstrap` **vazio** ou comentado pra não tentar resolver no AWS. User-secrets sobrescreve `appsettings`.

### (c) `appsettings.Development.json` direto

Hack rápido pra spike. **Nunca commitar** — `appsettings.Development.json` está gitignored.

```json
{
  "OpenAI": { "ApiKey": "sk-..." },
  "AzureAI": { "ApiKey": "..." }
}
```

### Docker compose

O compose monta `.env` como env vars no container. Variables como `POSTGRES_PASSWORD` viram parte da connection string montada inline no compose. Em dev é OK; em prod a connection inteira deve ir pelo Bootstrap.

---

## 6. Health check

```http
GET /api/system/health/circuit-breakers
```

Não é healthcheck de secrets diretamente, mas se uma chamada LLM falha por chave inválida, o circuit breaker abre. Pra validar especificamente que o app conseguiu resolver Bootstrap:

```bash
docker compose logs backend | grep -i "Bootstrap\|secret"
```

Procure por `Failed to resolve bootstrap secret '<chave>'` — mensagem fail-fast indica qual referência quebrou.

---

## 7. Troubleshooting

| Sintoma | Causa provável | Onde olhar / o que fazer |
|---|---|---|
| `Failed to resolve bootstrap secret 'X' (reference 'secret://aws/...')` no startup | Secret não existe no AWS, ou IAM não tem permissão | Conferir nome no AWS Console + IAM policy. App **não sobe** sem o secret resolvido. |
| Backend sobe mas LLM retorna 401/403 | Secret resolveu mas conteúdo é chave inválida ou expirada | Trocar valor no Secrets Manager. App pega na próxima request (ou dentro de `L1TtlSeconds`). |
| `Agent 'X': Foundry Responses requer ApiKey` em runtime | `Provider.ApiKey` do agent é null E não há fallback Bootstrap configurado | Definir `AzureAI:ApiKey` no Bootstrap, ou setar `Provider.ApiKey = "secret://aws/..."` no AgentDefinition. |
| Latência alta pra resolver secret | Cache vazio (cold start) ou TTL muito baixo | Aumentar `L1TtlSeconds`/`L2TtlSeconds`. Em prod usar Redis (L2) compartilhado entre instâncias. |
| Funciona em dev mas falha em prod | Perfil AWS local difere do role do pod | Conferir `aws sts get-caller-identity` em dev vs role assumido pelo pod (no Console ECS/K8s). |
| Rotation deixou de propagar | Cache L2 não invalidou | Reduzir `L2TtlSeconds` ou implementar invalidação manual via Redis `DEL secret:*`. |

---

## 8. Checklist — pré-deploy de novo ambiente

1. [ ] AWS Secrets Manager region escolhida (`us-east-1` é o default).
2. [ ] Secrets criadas com prefixo `efs-ai-hub/`:
   - [ ] `efs-ai-hub/postgres`
   - [ ] `efs-ai-hub/openai-default` (se OpenAI direto for usado)
   - [ ] `efs-ai-hub/azureai-default` (se Foundry for usado)
   - [ ] `efs-ai-hub/document-intelligence` (se Document Intelligence for usado)
   - [ ] `efs-ai-hub/azure-sp-{tenant,client,client-secret}-id` (se SP Azure for usado)
3. [ ] IAM role do pod/task com `secretsmanager:GetSecretValue` no prefixo `efs-ai-hub/*`.
4. [ ] `appsettings.{Env}.json` da imagem deployada com `Secrets:Bootstrap` apontando pras references.
5. [ ] `Secrets:Aws:Region` setado no mesmo `appsettings`.
6. [ ] Logs de startup confirmam resolução (sem `Failed to resolve bootstrap secret`).
7. [ ] Health check `/api/system/health/circuit-breakers` retorna 200 e todos os providers em `Closed`.

---

## 9. Não faça

- ❌ **Commitar secret value** em qualquer arquivo do repo. `appsettings.Development.json` está gitignored, mas user-secrets é o caminho certo pra dev.
- ❌ **Reusar a mesma secret entre tenants/projetos** quando o requisito de isolamento exige separação. Use `SecretContext.Project` e secrets dedicados.
- ❌ **Bypass do resolver chamando AWS SDK direto** em código de feature. Use `ISecretResolver` injetado — é o que aplica cache/retry/scope correto.
- ❌ **Setar `Resource: *`** na IAM policy. Restrinja por prefixo.
- ❌ **Cachear localmente o valor resolvido em variável estática**. O cache é gerido pelo `SecretCacheService` com TTL controlado.
