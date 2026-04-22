# MCP Servers (Model Context Protocol)

Registry centralizado de servidores MCP consumíveis pelos agentes da plataforma. Substitui a configuração inline que antes vivia dentro do JSONB de cada `agent_definitions`.

## Conceito

**Model Context Protocol (MCP)** é um protocolo aberto para expor tools/ferramentas a agentes LLM por HTTP. Exemplos práticos: um MCP server que expõe `read_file`/`list_dir` do filesystem local, um que fala com GitHub, um que lê Linear, etc.

No EfsAiHub o MCP server vira um **recurso de primeira classe**: cadastrado uma vez em `/mcp-servers`, pode ser referenciado por qualquer agent do mesmo projeto por `McpServerId`. Alterar o registro (URL, allowedTools, headers) atinge em runtime todos os agents que apontam para ele — **resolução live**, não snapshot.

## Contrato

Domain class: [`McpServer`](../src/EfsAiHub.Core.Agents/McpServers/McpServer.cs).

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | string(128) | ✅ | Identificador estável (ex: `mcp-filesystem`). |
| `name` | string(256) | ✅ | Nome humano exibido na UI. |
| `description` | string | opcional | Texto livre. |
| `serverLabel` | string | ✅ | Label enviado ao provider Azure Foundry. |
| `serverUrl` | string (http/https) | ✅ | URL absoluta do endpoint MCP. |
| `allowedTools` | string[] | ✅ (≥1 item) | Whitelist de tools que os agents podem invocar. |
| `headers` | map<string,string> | opcional | Headers HTTP (ex: `Authorization: Bearer ...`). |
| `requireApproval` | `"never"` \| `"always"` | default `"never"` | Política HITL. |
| `projectId` | string | auto | Preenchido pelo contexto — project-scoped. |

Persistência: [`aihub.mcp_servers`](../db/schema.sql) — coluna `Data` JSONB com o objeto serializado, colunas denormalizadas (`Id`, `Name`, `ProjectId`, `CreatedAt`, `UpdatedAt`) para indexação.

Exemplo de JSON:

```json
{
  "id": "mcp-filesystem",
  "name": "Filesystem MCP",
  "description": "Acesso leitura ao FS local",
  "serverLabel": "filesystem",
  "serverUrl": "http://localhost:3030",
  "allowedTools": ["read_file", "list_dir"],
  "headers": { "Authorization": "Bearer ..." },
  "requireApproval": "never"
}
```

## CRUD via API

Endpoints em `/api/admin/mcp-servers` (admin-gated):

| Método | Path | Resposta | Notas |
|---|---|---|---|
| GET | `/` | `{items, total, page, pageSize}` | Paginado; filtrado pelo projeto atual. |
| GET | `/{id}` | `McpServer` ou 404 | — |
| POST | `/` | `201 + McpServer` | Retorna 409 se Id já existe. |
| PUT | `/{id}` | `200 + McpServer` | `projectId` e `createdAt` são preservados. |
| DELETE | `/{id}` | `204` | Não cascateia — tools dos agents viram dangling (log warning). |

Cada mudança grava linha em `aihub.admin_audit_log` com `resourceType=mcp_server` (ver [PR admin_audit_log](./plataforma.md#admin-audit-trail)).

## Associando um MCP a um Agent

Via UI (`/agents/:id` → aba **Configuração** → seção **MCP Tools**):
- Dropdown com checkboxes listando os MCP servers do projeto.
- Selecionar um registro adiciona `{ "type": "mcp", "mcpServerId": "<id>" }` em `agent.tools`.

Via API:

```json
PUT /api/agents/my-agent
{
  "id": "my-agent",
  ...,
  "tools": [
    { "type": "function", "name": "buscar_ativo" },
    { "type": "mcp", "mcpServerId": "mcp-filesystem" }
  ]
}
```

## Como é resolvido em runtime

[`AzureFoundryClientProvider`](../src/EfsAiHub.Infra.LlmProviders/Providers/AzureFoundryClientProvider.cs) chama `FoundryToolBuilder.BuildAsync` para cada agent. Quando encontra `type=mcp`:

1. **id-based (preferido)**: se `McpServerId != null`, busca registro via `IMcpServerRepository.GetByIdAsync`. Se não achar → **log warning** e pula a tool (dangling).
2. **legacy/fallback**: se `McpServerId == null` mas `ServerLabel` + `ServerUrl` inline presentes, usa direto. Mantém BC com agents seedados antes do registry.

Monta `MCPToolDefinition(ServerLabel, ServerUrl)` + `AllowedTools` do registro. `Headers` ficam disponíveis para uso futuro (o SDK atual do Azure Foundry não os injeta automaticamente).

## Sem validação de rede na criação

**Diferença importante vs versões anteriores:** o `McpHealthChecker` foi **removido** da plataforma. Cadastrar um MCP offline é permitido — você consegue criar o registro e o agent mesmo com o MCP temporariamente fora do ar. A falha só aparece no momento da execução (se realmente houver), com log no backend.

Motivação: health check síncrono no caminho de create bloqueava usuários em flakiness de rede/MCP e tornava infra de dev complicada.

## Troubleshooting

| Sintoma | Causa provável | Resolução |
|---|---|---|
| "MCP não aparece no dropdown do agent" | Registro está em outro projeto | Use o projeto correto no header `x-efs-project-id` ou recadastre. |
| "Tool MCP sumiu do agent em runtime" | MCP foi deletado (dangling) | Recriar o registro com o mesmo Id OU atualizar o agent para usar outro. |
| "Mudei a URL e a execução usa a antiga" | Pod com cache stale — improvável porque é resolução live | Verifique se não há proxy/CDN intermediário cacheando. |
| "Headers Authorization não estão sendo enviados" | Limitação do SDK Azure Foundry — `MCPToolDefinition` ainda não recebe headers | Backlog `BC-MCP-HEADERS`; por ora use MCPs que aceitam auth via query/URL. |
| "Queria validar um MCP antes de cadastrar" | Feature removida | Teste manualmente com `curl $ServerUrl/health` antes. |

## Backlogs relacionados

- **BC-MCP-1**: migration script para converter agents com MCP inline para id-based (refs ao registry).
- **BC-MCP-HEADERS**: injetar `Headers` do registro no request ao MCP server (depende de update do SDK).
- **BC-SEC-1**: encriptação at-rest dos `Headers` via Data Protection (hoje plaintext em JSONB, mesmo tratamento de `projects.llm_config.ApiKey`).
