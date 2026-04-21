// Phase 8 — Host.Worker global usings. Namespaces preservados (ADR).
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.AI;

global using EfsAiHub.Core.Agents;
global using EfsAiHub.Core.Abstractions.Conversations;
global using EfsAiHub.Core.Orchestration.Enums;
global using EfsAiHub.Core.Abstractions.Observability;
global using EfsAiHub.Core.Agents.Responses;
global using EfsAiHub.Core.Agents.Skills;
global using EfsAiHub.Core.Orchestration.Workflows;
global using EfsAiHub.Core.Orchestration.Interfaces;
global using EfsAiHub.Core.Orchestration.Models;
global using EfsAiHub.Core.Orchestration.Executors;
global using EfsAiHub.Platform.Runtime.BackgroundServices;
global using EfsAiHub.Platform.Runtime.Configuration;
global using EfsAiHub.Platform.Runtime.Execution;
global using EfsAiHub.Platform.Runtime.Factories;
global using EfsAiHub.Platform.Runtime.Interfaces;
global using EfsAiHub.Platform.Runtime.Services;
global using EfsAiHub.Infra.Persistence.Cache;
global using EfsAiHub.Infra.Observability;
global using EfsAiHub.Infra.Observability.Services;
