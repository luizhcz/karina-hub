import { useEffect, useState } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { Onboarding } from './routes/Onboarding'
import { Layout } from './components/Layout'
import { ToolsList } from './routes/ToolsList'
import { ToolEditor } from './routes/ToolEditor'
import { McpServersList } from './routes/McpServersList'
import { McpServerEditor } from './routes/McpServerEditor'
import { AgentsList } from './routes/AgentsList'
import { AgentEditor } from './routes/AgentEditor'
import { getIdentity, subscribeIdentity } from './stores/identity'

export function App() {
  const [identity, setLocalIdentity] = useState(() => getIdentity())

  useEffect(() => subscribeIdentity(() => setLocalIdentity(getIdentity())), [])

  // Considera "logado" só quando o user já escolheu um projeto. Identity
  // provisória (name+account sem projectId) é salva enquanto o user preenche
  // o onboarding pra que o client.ts envie x-efs-account no listProjects, mas
  // não troca de tela ainda.
  if (!identity || !identity.projectId) {
    return (
      <Routes>
        <Route path="*" element={<Onboarding />} />
      </Routes>
    )
  }

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/agentes" replace />} />
        <Route path="/agentes" element={<AgentsList />} />
        <Route path="/agentes/novo" element={<AgentEditor mode="create" />} />
        <Route path="/agentes/:id" element={<AgentEditor mode="edit" />} />
        <Route path="/ferramentas" element={<ToolsList />} />
        <Route path="/ferramentas/nova" element={<ToolEditor mode="create" />} />
        <Route path="/ferramentas/:id" element={<ToolEditor mode="edit" />} />
        <Route path="/mcps" element={<McpServersList />} />
        <Route path="/mcps/novo" element={<McpServerEditor mode="create" />} />
        <Route path="/mcps/:id" element={<McpServerEditor mode="edit" />} />
        <Route path="*" element={<Navigate to="/agentes" replace />} />
      </Route>
    </Routes>
  )
}
