import { useEffect, useState } from 'react'
import { Navigate, Outlet, Route, Routes } from 'react-router'
import { Onboarding } from './routes/Onboarding'
import { Layout } from './components/Layout'
import { RequireAccessOrWelcome } from './components/RequireAccessOrWelcome'
import { ToolsList } from './routes/ToolsList'
import { ToolEditor } from './routes/ToolEditor'
import { McpServersList } from './routes/McpServersList'
import { McpServerEditor } from './routes/McpServerEditor'
import { RouterIntentsList } from './routes/RouterIntentsList'
import { RouterIntentEditor } from './routes/RouterIntentEditor'
import { AgentsList } from './routes/AgentsList'
import { AgentEditor } from './routes/AgentEditor'
import { AgentVersions } from './routes/AgentVersions'
import { AgentDeploy } from './routes/AgentDeploy'
import { RouterIntentPredictor } from './routes/RouterIntentPredictor'
import { Implantacoes } from './routes/Implantacoes'
import { PipelineEditor } from './routes/PipelineEditor'
import { RoutingDeployEditor } from './routes/RoutingDeployEditor'
import { ChatDeployEditor } from './routes/ChatDeployEditor'
import { ChatDeploymentSandbox } from './routes/ChatDeploymentSandbox'
import { DeploymentSandbox } from './routes/DeploymentSandbox'
import { Aprovacoes } from './routes/Aprovacoes'
import { Avaliacoes } from './routes/Avaliacoes'
import { Dashboard } from './routes/Dashboard'
import { Welcome } from './routes/Welcome'
import { UsuariosList } from './routes/admin/UsuariosList'
import { AuditoriaList } from './routes/admin/AuditoriaList'
import { MiddlewaresList } from './routes/admin/MiddlewaresList'
import { LlmCaptureControl } from './routes/admin/LlmCaptureControl'
import { LlmCallsList } from './routes/admin/LlmCallsList'
import { LlmCallDetailPage } from './routes/admin/LlmCallDetail'
import { getIdentity, subscribeIdentity } from './stores/identity'

export function App() {
  const [identity, setLocalIdentity] = useState(() => getIdentity())

  useEffect(() => subscribeIdentity(() => setLocalIdentity(getIdentity())), [])

  // Considera "logado" só quando o user já confirmou nome + conta. ProjectId
  // pode ficar vazio quando o user não tem nenhum projeto vinculado — nesse
  // caso o Welcome.tsx + RequireAccessOrWelcome cuidam do redirect.
  if (!identity || !identity.account) {
    return (
      <Routes>
        <Route path="*" element={<Onboarding />} />
      </Routes>
    )
  }

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/bem-vindo" element={<Welcome />} />
        {/* Rotas admin-only: gating é da página + AdminGate backend. */}
        <Route path="/admin/usuarios" element={<UsuariosList />} />
        <Route path="/admin/auditoria" element={<AuditoriaList />} />
        <Route path="/admin/middlewares" element={<MiddlewaresList />} />
        <Route path="/admin/llm-capture" element={<LlmCaptureControl />} />
        <Route path="/admin/llm-calls" element={<LlmCallsList />} />
        <Route path="/admin/llm-calls/:id" element={<LlmCallDetailPage />} />

        {/* Rotas que exigem projeto vinculado: redirecionam pra /bem-vindo
            quando non-admin tem projects=[]. Admin passa sempre. */}
        <Route element={<GuardedOutlet />}>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/agentes" element={<AgentsList />} />
          <Route path="/agentes/novo" element={<AgentEditor mode="create" />} />
          <Route path="/agentes/:id/versoes" element={<AgentVersions />} />
          <Route path="/agentes/:id/implantar" element={<AgentDeploy />} />
          <Route path="/agentes/:id/predict" element={<RouterIntentPredictor />} />
          <Route path="/implantacoes" element={<Implantacoes />} />
          <Route path="/implantacoes/avancada" element={<PipelineEditor />} />
          <Route path="/implantacoes/avancada/:id" element={<PipelineEditor />} />
          <Route path="/implantacoes/roteamento" element={<RoutingDeployEditor />} />
          <Route path="/implantacoes/roteamento/:id" element={<RoutingDeployEditor />} />
          <Route path="/implantacoes/chat" element={<ChatDeployEditor />} />
          <Route path="/implantacoes/chat/:id" element={<ChatDeployEditor />} />
          <Route path="/implantacoes/chat/:id/sandbox" element={<ChatDeploymentSandbox />} />
          <Route path="/implantacoes/:id/sandbox" element={<DeploymentSandbox />} />
          <Route path="/avaliacoes" element={<Avaliacoes />} />
          <Route path="/aprovacoes" element={<Aprovacoes />} />
          <Route path="/agentes/:id" element={<AgentEditor mode="edit" />} />
          <Route path="/ferramentas" element={<ToolsList />} />
          <Route path="/ferramentas/nova" element={<ToolEditor mode="create" />} />
          <Route path="/ferramentas/:id" element={<ToolEditor mode="edit" />} />
          <Route path="/mcps" element={<McpServersList />} />
          <Route path="/mcps/novo" element={<McpServerEditor mode="create" />} />
          <Route path="/mcps/:id" element={<McpServerEditor mode="edit" />} />
          <Route path="/intencoes" element={<RouterIntentsList />} />
          <Route path="/intencoes/nova" element={<RouterIntentEditor mode="create" />} />
          <Route path="/intencoes/:id" element={<RouterIntentEditor mode="edit" />} />
          <Route path="*" element={<Navigate to="/agentes" replace />} />
        </Route>
      </Route>
    </Routes>
  )
}

function GuardedOutlet() {
  return (
    <RequireAccessOrWelcome>
      <Outlet />
    </RequireAccessOrWelcome>
  )
}
