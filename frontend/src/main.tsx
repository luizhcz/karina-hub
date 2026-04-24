import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AppProviders } from './providers'
import './index.css'
// F8 — inicializa i18n antes de qualquer render. Side-effect import: o
// módulo roda i18n.init() na importação.
import './i18n'

// Force Monaco to use local node_modules instead of CDN (cdn.jsdelivr.net)
import loader from '@monaco-editor/loader'
import * as monaco from 'monaco-editor'
loader.config({ monaco })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders />
  </StrictMode>
)
