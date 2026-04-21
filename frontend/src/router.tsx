import { createBrowserRouter, Navigate } from 'react-router'
import { AppShell } from './app/AppShell'
import { AuthGuard } from './app/AuthGuard'
import { NotFound } from './app/NotFound'
import { LoginPage } from './features/auth/LoginPage'

// Dashboard
import { DashboardPage } from './features/dashboard/DashboardPage'

// Agents
import { AgentsListPage } from './features/agents/AgentsListPage'
import { AgentCreatePage } from './features/agents/AgentCreatePage'
import { AgentDetailPage } from './features/agents/AgentDetailPage'

// Workflows
import { WorkflowsListPage } from './features/workflows/WorkflowsListPage'
import { WorkflowCreatePage } from './features/workflows/WorkflowCreatePage'
import { WorkflowEditPage } from './features/workflows/WorkflowEditPage'
import { WorkflowDiagramPage } from './features/workflows/WorkflowDiagramPage'
import { WorkflowVersionsPage } from './features/workflows/WorkflowVersionsPage'
import { WorkflowTriggerPage } from './features/workflows/WorkflowTriggerPage'
import { WorkflowSandboxPage } from './features/workflows/WorkflowSandboxPage'

// Chat
import { ChatPage } from './features/chat/ChatPage'
import { ChatWindowPage } from './features/chat/ChatWindowPage'
import { ConversationListPage } from './features/chat/ConversationListPage'

// Executions
import { ExecutionsListPage } from './features/executions/ExecutionsListPage'
import { ExecutionDetailPage } from './features/executions/ExecutionDetailPage'

// HITL
import { HitlPendingPage } from './features/hitl/HitlPendingPage'
import { HitlResolvePage } from './features/hitl/HitlResolvePage'
import { HitlHistoryPage } from './features/hitl/HitlHistoryPage'

// Tools & Skills
import { ToolsListPage } from './features/tools/ToolsListPage'
import { SkillsListPage } from './features/skills/SkillsListPage'
import { SkillCreatePage } from './features/skills/SkillCreatePage'
import { SkillEditPage } from './features/skills/SkillEditPage'

// Observability
import { MetricsOverviewPage } from './features/metrics/MetricsOverviewPage'
import { MetricsByAgentPage } from './features/metrics/MetricsByAgentPage'
import { MetricsByProviderPage } from './features/metrics/MetricsByProviderPage'
import { TracingListPage } from './features/tracing/TracingListPage'
import { TracingDetailPage } from './features/tracing/TracingDetailPage'
import { AuditEventsPage } from './features/audit/AuditEventsPage'
import { AuditTrailPage } from './features/audit/AuditTrailPage'
import { TokenUsageAuditPage } from './features/audit/TokenUsageAuditPage'

// Admin
import { CostLayout } from './features/costs/CostLayout'
import { CostDashboardPage } from './features/costs/CostDashboardPage'
import { ModelPricingPage } from './features/costs/ModelPricingPage'
import { ModelCatalogPage } from './features/costs/ModelCatalogPage'
import { WorkflowCostPage } from './features/costs/WorkflowCostPage'
import { ProjectCostPage } from './features/costs/ProjectCostPage'
import { ProjectsListPage } from './features/projects/ProjectsListPage'
import { ProjectCreatePage } from './features/projects/ProjectCreatePage'
import { ProjectEditPage } from './features/projects/ProjectEditPage'
import { ProjectStatsPage } from './features/projects/ProjectStatsPage'
import { ConfigPage } from './features/config/ConfigPage'
import { BackgroundJobsPage } from './features/background/BackgroundJobsPage'
import { NewJobPage } from './features/background/NewJobPage'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    path: '/',
    element: <AuthGuard />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <Navigate to="/dashboard" replace /> },

      // Dashboard
      { path: 'dashboard', element: <DashboardPage /> },

      // Agents
      { path: 'agents', element: <AgentsListPage /> },
      { path: 'agents/new', element: <AgentCreatePage /> },
      { path: 'agents/:id', element: <AgentDetailPage /> },
      { path: 'agents/:id/versions', element: <AgentDetailPage initialTab="versions" /> },
      { path: 'agents/:id/sandbox', element: <AgentDetailPage initialTab="sandbox" /> },
      { path: 'agents/:id/prompts', element: <AgentDetailPage initialTab="prompts" /> },

      // Workflows
      { path: 'workflows', element: <WorkflowsListPage /> },
      { path: 'workflows/new', element: <WorkflowCreatePage /> },
      { path: 'workflows/:id', element: <WorkflowEditPage /> },
      { path: 'workflows/:id/diagram', element: <WorkflowDiagramPage /> },
      { path: 'workflows/:id/versions', element: <WorkflowVersionsPage /> },
      { path: 'workflows/:id/trigger', element: <WorkflowTriggerPage /> },
      { path: 'workflows/:id/sandbox', element: <WorkflowSandboxPage /> },

      // Chat
      { path: 'chat', element: <ChatPage /> },
      { path: 'chat/:id', element: <ChatWindowPage /> },
      { path: 'conversations', element: <ConversationListPage /> },

      // Executions
      { path: 'executions', element: <ExecutionsListPage /> },
      { path: 'executions/:id', element: <ExecutionDetailPage /> },

      // HITL
      { path: 'hitl', element: <HitlPendingPage /> },
      { path: 'hitl/history', element: <HitlHistoryPage /> },
      { path: 'hitl/:id', element: <HitlResolvePage /> },

      // Tools & Skills
      { path: 'tools', element: <ToolsListPage /> },
      { path: 'skills', element: <SkillsListPage /> },
      { path: 'skills/new', element: <SkillCreatePage /> },
      { path: 'skills/:id', element: <SkillEditPage /> },

      // Observability
      { path: 'metrics', element: <MetricsOverviewPage /> },
      { path: 'metrics/agents', element: <MetricsByAgentPage /> },
      { path: 'metrics/providers', element: <MetricsByProviderPage /> },
      { path: 'tracing', element: <TracingListPage /> },
      { path: 'tracing/:traceId', element: <TracingDetailPage /> },
      { path: 'audit', element: <AuditEventsPage /> },
      { path: 'audit/trail', element: <AuditTrailPage /> },
      { path: 'audit/tokens', element: <TokenUsageAuditPage /> },

      // Admin — Custos (layout com tabs)
      {
        path: 'costs',
        element: <CostLayout />,
        children: [
          { index: true, element: <CostDashboardPage /> },
          { path: 'workflows', element: <WorkflowCostPage /> },
          { path: 'projects', element: <ProjectCostPage /> },
          { path: 'pricing', element: <ModelPricingPage /> },
          { path: 'model-catalog', element: <ModelCatalogPage /> },
        ],
      },
      { path: 'projects', element: <ProjectsListPage /> },
      { path: 'projects/new', element: <ProjectCreatePage /> },
      { path: 'projects/:id', element: <ProjectEditPage /> },
      { path: 'projects/:id/stats', element: <ProjectStatsPage /> },
      { path: 'config', element: <ConfigPage /> },
      { path: 'background', element: <BackgroundJobsPage /> },
      { path: 'background/new', element: <NewJobPage /> },

      // 404
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
])
