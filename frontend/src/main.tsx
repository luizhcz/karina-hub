import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AppProviders } from './providers'
import './index.css'
// Inicializa i18n antes do render — side-effect import roda i18n.init()
import './i18n'

// Força Monaco a usar node_modules local (evita baixar do cdn.jsdelivr.net)
import loader from '@monaco-editor/loader'
import * as monaco from 'monaco-editor'
loader.config({ monaco })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders />
  </StrictMode>
)
