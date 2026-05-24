import { lazy, Suspense, useEffect, useState } from 'react'
import { Navigate, Outlet, Route, Routes } from 'react-router'
import { Onboarding } from './routes/Onboarding'
import { Layout } from './components/Layout'
import { RequireAccessOrWelcome } from './components/RequireAccessOrWelcome'
import { getIdentity, subscribeIdentity } from './stores/identity'
import { readAccessToken } from './auth/headers'
import { useMe } from './stores/me'

// Eager: rotas leves que aparecem no first-paint do usuário típico
// (dashboard, lista de agentes, welcome). Splash de Suspense fica reservado
// pra deeper navigations onde o ganho de cache compensa o flicker inicial.
import { Dashboard } from './routes/Dashboard'
import { Welcome } from './routes/Welcome'
import { AgentsList } from './routes/AgentsList'
import { Implantacoes } from './routes/Implantacoes'

// Lazy: rotas que arrastam vendor pesado (BlockNote, markdown, editores
// complexos) ou pouco frequentadas (admin, sandbox). Cada uma vira chunk
// próprio + entra em cache long-term ao primeiro acesso.
const AgentEditor = lazy(() =>
  import('./routes/AgentEditor').then(m => ({ default: m.AgentEditor })))
const AgentVersions = lazy(() =>
  import('./routes/AgentVersions').then(m => ({ default: m.AgentVersions })))
const AgentDeploy = lazy(() =>
  import('./routes/AgentDeploy').then(m => ({ default: m.AgentDeploy })))
const RouterIntentPredictor = lazy(() =>
  import('./routes/RouterIntentPredictor').then(m => ({ default: m.RouterIntentPredictor })))
const PipelineEditor = lazy(() =>
  import('./routes/PipelineEditor').then(m => ({ default: m.PipelineEditor })))
const RoutingDeployEditor = lazy(() =>
  import('./routes/RoutingDeployEditor').then(m => ({ default: m.RoutingDeployEditor })))
const ChatDeployEditor = lazy(() =>
  import('./routes/ChatDeployEditor').then(m => ({ default: m.ChatDeployEditor })))
const ChatDeploymentSandbox = lazy(() =>
  import('./routes/ChatDeploymentSandbox').then(m => ({ default: m.ChatDeploymentSandbox })))
const DeploymentSandbox = lazy(() =>
  import('./routes/DeploymentSandbox').then(m => ({ default: m.DeploymentSandbox })))
const Aprovacoes = lazy(() =>
  import('./routes/Aprovacoes').then(m => ({ default: m.Aprovacoes })))
const Avaliacoes = lazy(() =>
  import('./routes/Avaliacoes').then(m => ({ default: m.Avaliacoes })))
const ToolsList = lazy(() =>
  import('./routes/ToolsList').then(m => ({ default: m.ToolsList })))
const ToolEditor = lazy(() =>
  import('./routes/ToolEditor').then(m => ({ default: m.ToolEditor })))
const RouterIntentsList = lazy(() =>
  import('./routes/RouterIntentsList').then(m => ({ default: m.RouterIntentsList })))
const RouterIntentEditor = lazy(() =>
  import('./routes/RouterIntentEditor').then(m => ({ default: m.RouterIntentEditor })))

// Admin: baixa frequência, pode ficar lazy sem penalty visível.
const UsuariosList = lazy(() =>
  import('./routes/admin/UsuariosList').then(m => ({ default: m.UsuariosList })))
const AuditoriaList = lazy(() =>
  import('./routes/admin/AuditoriaList').then(m => ({ default: m.AuditoriaList })))
const MiddlewaresList = lazy(() =>
  import('./routes/admin/MiddlewaresList').then(m => ({ default: m.MiddlewaresList })))
const LlmCaptureControl = lazy(() =>
  import('./routes/admin/LlmCaptureControl').then(m => ({ default: m.LlmCaptureControl })))
const LlmCallsList = lazy(() =>
  import('./routes/admin/LlmCallsList').then(m => ({ default: m.LlmCallsList })))
const LlmCallDetailPage = lazy(() =>
  import('./routes/admin/LlmCallDetail').then(m => ({ default: m.LlmCallDetailPage })))

export function App() {
  const [identity, setLocalIdentity] = useState(() => getIdentity())

  useEffect(() => subscribeIdentity(() => setLocalIdentity(getIdentity())), [])

  // 3 caminhos:
  //   1. Identity completa (account presente) → app normal.
  //   2. Sem identity + access_token salvo (fluxo via proxy/URL) → dispara /me
  //      via useMe pra hidratar identity; exibe splash enquanto isso. /me com
  //      accountId chama setIdentity (stores/me.ts → syncIdentityFromMe).
  //   3. Sem identity + sem token (dev local sem proxy) → Onboarding manual.
  if (!identity || !identity.account) {
    if (readAccessToken()) {
      return <BootstrappingIdentity />
    }
    return (
      <Routes>
        <Route path="*" element={<Onboarding />} />
      </Routes>
    )
  }

  return (
    <Suspense fallback={<RouteLoading />}>
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
            <Route path="/intencoes" element={<RouterIntentsList />} />
            <Route path="/intencoes/nova" element={<RouterIntentEditor mode="create" />} />
            <Route path="/intencoes/:id" element={<RouterIntentEditor mode="edit" />} />
            <Route path="*" element={<Navigate to="/agentes" replace />} />
          </Route>
        </Route>
      </Routes>
    </Suspense>
  )
}

/**
 * Fallback enquanto o chunk da rota está sendo baixado (rede lenta). Cobre o
 * caso da primeira navegação pra uma rota lazy — em redes rápidas, o flash é
 * imperceptível. Reutiliza a paleta do Layout pra evitar layout shift.
 */
function RouteLoading() {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="text-sm text-fg-muted">Carregando…</div>
    </div>
  )
}

function GuardedOutlet() {
  return (
    <RequireAccessOrWelcome>
      <Outlet />
    </RequireAccessOrWelcome>
  )
}

/**
 * Splash exibido enquanto o /me hidrata a identity após o bootstrap por URL.
 * Dispara o /me via useMe; quando a resposta volta com accountId, o store
 * (stores/me.ts → syncIdentityFromMe) chama setIdentity, o App re-renderiza
 * e cai no caminho normal. Se /me devolver accountId=null (proxy não
 * resolveu o token), o splash fica indefinido — fail-safe: melhor mostrar
 * "carregando" que cair num Onboarding enganoso pra quem veio com SSO.
 */
function BootstrappingIdentity() {
  useMe()
  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="text-sm text-fg-muted">Carregando sessão…</div>
    </div>
  )
}
