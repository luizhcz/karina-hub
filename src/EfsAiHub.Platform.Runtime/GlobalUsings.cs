// Phase 5a — Platform.Runtime global usings. Espelha o GlobalUsings.cs do
// EfsAiHub.Api para tipos de domínio movidos para Core.Orchestration.
// Namespaces preservados (EfsAiHub.Api.*) conforme ADR do plano.

global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.Logging;
global using Microsoft.Agents.AI;
global using Microsoft.Agents.AI.Workflows;

global using EfsAiHub.Core.Agents;
global using EfsAiHub.Core.Orchestration.Enums;
global using EfsAiHub.Core.Abstractions.Observability;
global using EfsAiHub.Core.Agents.Skills;
global using EfsAiHub.Core.Agents.Trading;
global using EfsAiHub.Core.Orchestration.Workflows;
global using EfsAiHub.Platform.Runtime.Interfaces;
global using EfsAiHub.Core.Orchestration.Interfaces;
global using EfsAiHub.Core.Orchestration.Models;
global using EfsAiHub.Platform.Runtime.Services;
global using EfsAiHub.Core.Agents.Interfaces;
global using EfsAiHub.Core.Agents.Services;
global using EfsAiHub.Core.Agents.Execution;
global using EfsAiHub.Core.Orchestration.Executors;
global using EfsAiHub.Platform.Runtime.Factories;
global using EfsAiHub.Platform.Guards;
global using EfsAiHub.Platform.Runtime.Middlewares;
global using EfsAiHub.Platform.Runtime.Execution;
global using EfsAiHub.Core.Orchestration.Validation;
global using EfsAiHub.Infra.Persistence.Cache;
global using EfsAiHub.Platform.Runtime.BackgroundServices;
global using EfsAiHub.Platform.Runtime.Configuration;
global using EfsAiHub.Core.Orchestration.Coordination;
global using EfsAiHub.Infra.Observability;
global using EfsAiHub.Infra.Observability.Services;
global using EfsAiHub.Infra.Persistence.Checkpointing;
