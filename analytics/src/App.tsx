import { useEffect, useState } from 'react'
import { Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { Overview } from './routes/Overview'
import { Custos } from './routes/Custos'
import { Ferramentas } from './routes/Ferramentas'
import { Confiabilidade } from './routes/Confiabilidade'
import { Fila } from './routes/Fila'
import { Webhooks } from './routes/Webhooks'
import { Roteador } from './routes/Roteador'
import { Execucoes } from './routes/Execucoes'
import { Workers } from './routes/Workers'
import { Feedback } from './routes/Feedback'
import { DocumentIntelligence } from './routes/DocumentIntelligence'
import { AuditoriaList } from './routes/admin/AuditoriaList'
import { LlmCaptureControl } from './routes/admin/LlmCaptureControl'
import { LlmCallsList } from './routes/admin/LlmCallsList'
import { LlmCallDetailPage } from './routes/admin/LlmCallDetail'
import { Onboarding } from './routes/Onboarding'
import { getIdentity, subscribeIdentity } from './stores/identity'
import { readAccessToken } from './auth/headers'
import { useMe } from './stores/me'

// 3 caminhos de boot (mirror do mvp/):
//   1. Identity completa → app normal.
//   2. Sem identity + access_token salvo (fluxo via proxy/URL) → dispara /me
//      via useMe pra hidratar identity; splash enquanto isso. /me com
//      accountId chama setIdentity (stores/me.ts → syncIdentityFromMe).
//   3. Sem identity + sem token (dev local sem proxy) → Onboarding manual.
//      Mesmo fluxo do MVP: user digita name/account/userType, App re-renderiza
//      e cai no caminho normal. /me sobrescreve permissions/projects/tenant.

export function App() {
  const [identity, setLocalIdentity] = useState(() => getIdentity())
  useEffect(() => subscribeIdentity(() => setLocalIdentity(getIdentity())), [])

  if (!identity || !identity.account) {
    if (readAccessToken()) return <BootstrappingIdentity />
    return (
      <Routes>
        <Route path="*" element={<Onboarding />} />
      </Routes>
    )
  }

  return (
    <AuthenticatedApp />
  )
}

// Componente separado pra que useMe() só dispare DEPOIS da identity local
// estar populada. Antes disso, /me sem headers de account ia voltar
// accountId=null e bagunçar o syncIdentityFromMe (no-op, mas request inútil).
function AuthenticatedApp() {
  // /me sempre é disparado — mesmo com identity local, queremos sincronizar
  // permissions e projetos visíveis (caso o admin tenha mudado vínculos).
  useMe()

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Overview />} />
        <Route path="/custos" element={<Custos />} />
        <Route path="/ferramentas" element={<Ferramentas />} />
        <Route path="/confiabilidade" element={<Confiabilidade />} />
        <Route path="/fila" element={<Fila />} />
        <Route path="/webhooks" element={<Webhooks />} />
        <Route path="/roteador" element={<Roteador />} />
        <Route path="/execucoes" element={<Execucoes />} />
        <Route path="/workers" element={<Workers />} />
        <Route path="/feedback" element={<Feedback />} />
        <Route path="/document-intelligence" element={<DocumentIntelligence />} />
        <Route path="/admin/auditoria" element={<AuditoriaList />} />
        <Route path="/admin/llm-capture" element={<LlmCaptureControl />} />
        <Route path="/admin/llm-calls" element={<LlmCallsList />} />
        <Route path="/admin/llm-calls/:id" element={<LlmCallDetailPage />} />
      </Route>
    </Routes>
  )
}

function BootstrappingIdentity() {
  // Dispara /me pra que o splash termine quando a identity chegar via
  // syncIdentityFromMe (stores/me.ts). Sem useMe aqui, o splash ficaria preso
  // indefinidamente quando o user chegou via ?access_token=...
  useMe()
  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="text-sm text-fg-muted">Carregando sessão…</div>
    </div>
  )
}
