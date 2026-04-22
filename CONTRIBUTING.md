# Contributing to EfsAiHub

Thank you for your interest in contributing! This guide explains how to extend the three main plugin points of the system: **Function Tools**, **LLM Middlewares**, and **Code Executors**.

---

## Extension Points Overview

| Type | What it is | Activated by |
|---|---|---|
| **Function Tool** | A C# method callable by the LLM during a conversation | Agent definition → `tools` array |
| **LLM Middleware** | A decorator that intercepts every LLM call for a specific agent | Agent definition → `middlewares` array |
| **Code Executor** | A pure C# function (no LLM) used as a node in Graph workflows | Workflow definition → `executors` array + edges |

---

## 1. Adding a Function Tool

A Function Tool is a C# method that the LLM can call via tool-use. It must be decorated with `[Description]` attributes so the LLM knows what it does and what each parameter means.

### Step 1 — Implement the function

Create a class in `src/EfsAiHub.Platform.Runtime/Functions/` (or add to an existing one if it belongs to the same domain):

```csharp
using System.ComponentModel;

public class MyDomainFunctions
{
    [Description("Brief description of what this tool does — the LLM reads this.")]
    public async Task<string> MyTool(
        [Description("What this parameter means")] string inputParam,
        [Description("Optional limit")] int limit = 10)
    {
        // Your logic here. Return a JSON string or a plain string.
        var result = new { status = "ok", data = inputParam };
        return JsonSerializer.Serialize(result);
    }
}
```

**Rules:**
- Return type must be `string` or `Task<string>`
- Use `[Description]` on the method AND on every parameter — this becomes the LLM prompt
- Keep descriptions objective; the LLM uses them to decide when and how to call the tool

### Step 2 — Register it in Program.cs

Open `src/EfsAiHub.Host.Api/Program.cs` and find the `IFunctionToolRegistry` block (search for `Function Tool Registry`):

```csharp
// ── Function Tool Registry ───────────────────────────────────────────────
builder.Services.AddSingleton<IFunctionToolRegistry>(sp =>
{
    var registry = new FunctionToolRegistry();

    // existing registrations...

    // ADD YOUR TOOL HERE:
    var myFunctions = sp.GetRequiredService<MyDomainFunctions>();
    registry.Register("my_tool_name", AIFunctionFactory.Create(myFunctions.MyTool));

    return registry;
});
```

Also register `MyDomainFunctions` in DI if it needs injected services:

```csharp
builder.Services.AddSingleton<MyDomainFunctions>();
```

### Step 3 — Enable on an Agent

In the agent definition (via the admin UI or API), add the tool name to the `tools` array:

```json
{
  "tools": [{ "type": "function", "name": "my_tool_name" }]
}
```

That's it — the LLM can now call `my_tool_name` when talking to that agent.

---

## 2. Adding an LLM Middleware

An LLM Middleware is a `DelegatingChatClient` that wraps the LLM client for a specific agent. It can intercept messages **before** they go to the LLM and **after** the response comes back.

Use cases: rate limiting, PII masking, account validation, cost tracking, logging.

### Step 1 — Implement the middleware

Create a class in `src/EfsAiHub.Platform.Runtime/` extending `DelegatingChatClient`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

public class MyMiddlewareChatClient : DelegatingChatClient
{
    private readonly string _agentId;
    private readonly Dictionary<string, string> _settings;
    private readonly ILogger _logger;

    public MyMiddlewareChatClient(
        IChatClient innerClient,
        string agentId,
        Dictionary<string, string> settings,
        ILogger logger)
        : base(innerClient)
    {
        _agentId = agentId;
        _settings = settings;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // PRE: intercept messages before they reach the LLM
        _logger.LogDebug("MyMiddleware: intercepting request for agent {AgentId}", _agentId);

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // POST: intercept the LLM response
        _logger.LogDebug("MyMiddleware: intercepting response for agent {AgentId}", _agentId);

        return response;
    }
}
```

**Reading settings:** The `settings` dictionary comes from the agent definition's `middlewares[].settings` field. Example: `_settings.GetValueOrDefault("myOption", "default")`.

### Step 2 — Register it in Program.cs

Find the `IAgentMiddlewareRegistry` block:

```csharp
// ── Agent Middleware Registry ────────────────────────────────────────────
builder.Services.AddSingleton<IAgentMiddlewareRegistry>(sp =>
{
    var registry = new AgentMiddlewareRegistry();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AgentMiddlewareRegistry");

    // existing registrations...

    // ADD YOUR MIDDLEWARE HERE:
    registry.Register("MyMiddleware", (inner, agentId, settings, _) =>
        new MyMiddlewareChatClient(inner, agentId, settings, logger));

    return registry;
});
```

### Step 3 — Enable on an Agent

In the agent definition, add a middleware entry:

```json
{
  "middlewares": [{
    "type": "MyMiddleware",
    "enabled": true,
    "settings": { "myOption": "someValue" }
  }]
}
```

Middlewares stack in order — each one wraps the next, forming a pipeline before the LLM.

---

## 3. Adding a Code Executor

A Code Executor is a pure C# function (no LLM) that acts as a **node** in a Graph-mode workflow. It receives a string, does work, and returns a string. It is connected to other nodes via edges defined in the workflow.

Use cases: data fetch, CSV parsing, queue management, external API calls, validation.

### Step 1 — Implement the executor function

Create a static method or class in `src/EfsAiHub.Platform.Runtime/CodeExecutors/` (or `Functions/`):

```csharp
public static class MyExecutorFunctions
{
    public static async Task<string> ProcessData(string input, CancellationToken ct)
    {
        // Input is whatever the previous workflow node output as a string.
        // Parse it, process it, return the result as a string.
        var data = JsonSerializer.Deserialize<MyInputModel>(input);

        var result = await DoWorkAsync(data, ct);

        return JsonSerializer.Serialize(result);
    }
}
```

**Contract:** `Func<string, CancellationToken, Task<string>>` — input string → output string.

### Step 2 — Register it in Program.cs

Find the `ICodeExecutorRegistry` block:

```csharp
// ── Code Executor Registry ───────────────────────────────────────────────
builder.Services.AddSingleton<ICodeExecutorRegistry>(sp =>
{
    var registry = new CodeExecutorRegistry();

    // existing registrations...

    // ADD YOUR EXECUTOR HERE:
    registry.Register("my_executor_name",
        (input, ct) => MyExecutorFunctions.ProcessData(input, ct));

    return registry;
});
```

If your executor needs DI services, resolve them from `sp`:

```csharp
registry.Register("my_executor_name", async (input, ct) =>
{
    var repo = sp.GetRequiredService<IMyRepository>();
    return await MyExecutorFunctions.ProcessData(input, repo, ct);
});
```

### Step 3 — Add to a Workflow definition

In the workflow definition (via admin UI or API), add an executor node and connect it via edges:

```json
{
  "orchestrationMode": "Graph",
  "executors": [
    {
      "id": "my-node-id",
      "functionName": "my_executor_name",
      "description": "What this executor does"
    }
  ],
  "edges": [
    { "from": "previous-agent-id", "to": "my-node-id", "edgeType": "Direct" },
    { "from": "my-node-id", "to": "next-agent-id", "edgeType": "Direct" }
  ]
}
```

The `functionName` in the workflow definition **must match exactly** the name used in `registry.Register(...)`.

---

## Separating registrations into their own file

For large contributions, prefer creating a dedicated setup extension method instead of adding directly to `Program.cs`:

```csharp
// src/EfsAiHub.Host.Api/Setup/MyFeatureSetup.cs
public static class MyFeatureSetup
{
    public static void RegisterMyFeature(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<ICodeExecutorRegistry>();

        registry.Register("my_executor_name",
            (input, ct) => MyExecutorFunctions.ProcessData(input, ct));
    }
}
```

Then call it in `Program.cs` after `app.Build()`:

```csharp
var app = builder.Build();
app.RegisterMyFeature(); // <-- add here
```

See `src/EfsAiHub.Host.Api/AtivoExecutorSetup.cs` for a real example of this pattern.

---

## Domain entities — `Create()` + `EnsureInvariants()`

Entities in the core domain (`WorkflowDefinition`, `Project`, `AgentDefinition`) protect their invariants through two entry points:

1. **Imperative construction** (controllers, application services, clones): always use `Create(...)`. It runs `EnsureInvariants()` automatically and throws `DomainException` if any rule is violated.
2. **Deserialization from storage** (repositories reading from Postgres JSONB): use `JsonSerializer.Deserialize<T>(json, JsonDefaults.Domain)` as usual. Then call `entity.EnsureInvariants()` explicitly if you want to revalidate the stored state.

`DomainException` is mapped to HTTP 400 by `GlobalExceptionMiddleware` — client receives a clean message when a rule is violated at the API boundary.

Example:
```csharp
// Controller receiving a new workflow
var wf = WorkflowDefinition.Create(
    id: request.Id,
    name: request.Name,
    orchestrationMode: request.Mode,
    agents: request.Agents,
    edges: request.Edges);   // throws DomainException → 400 if invalid
```

Collections on these entities are exposed as `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` to prevent post-construction mutation. If you need to modify, construct a new entity via `Create`.

---

## Workflow event handlers (internal extension point)

Some framework events (from `Microsoft.Agents.AI.Workflows`) are routed through `WorkflowRunnerService.HandleEventAsync`. When a specific event type needs non-trivial logic, extract it into a dedicated handler class in `src/EfsAiHub.Host.Worker/Services/EventHandlers/`.

**Pattern (`AgentHandoffEventHandler` is the reference implementation):**

1. Create a `sealed class XyzEventHandler` in `EventHandlers/` with a single public method `HandleAsync(TEvent evt, WorkflowExecution execution, NodeStateTracker tracker, ...agentNames, CancellationToken ct)`.
2. Inject only what you need via constructor — typically `INodeExecutionRepository`, `IWorkflowEventBus`, `TokenBatcher`, `ILogger<>`. Keep `NodeStateTracker` as a method parameter (it's per-execution state, not DI-registered).
3. Register in `ServiceCollectionExtensions` as scoped next to `WorkflowRunnerCollaborators`:
   ```csharp
   services.AddScoped<EventHandlers.XyzEventHandler>();
   ```
4. Add the handler as a field in `WorkflowRunnerCollaborators` record.
5. Replace the `switch` case in `WorkflowRunnerService.HandleEventAsync` with a single `await _xyzHandler.HandleAsync(...)` call.
6. Add unit tests in `tests/EfsAiHub.Tests.Unit/Workers/` mocking `INodeExecutionRepository` + `IWorkflowEventBus`.

This keeps `WorkflowRunnerService` focused on orchestration (event loop, timeout, metrics aggregation) while delegating event-specific behavior to small testable units.

---

## Where to find examples

| Extension Type | Example Implementation | Example Registration |
|---|---|---|
| Function Tool | `src/.../Functions/BoletaToolFunctions.cs` | `Program.cs` → `IFunctionToolRegistry` block |
| LLM Middleware | `src/.../Guards/AccountGuardChatClient.cs` | `Program.cs` → `IAgentMiddlewareRegistry` block |
| Code Executor | `src/.../CodeExecutors/` | `AtivoExecutorSetup.cs` |

## Questions?

Open an issue or start a Discussion. Please include the workflow/agent definition JSON when reporting bugs.
