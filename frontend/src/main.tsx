import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AppProviders } from './providers'
import './index.css'

// Force Monaco to use local node_modules instead of CDN (cdn.jsdelivr.net)
import loader from '@monaco-editor/loader'
import * as monaco from 'monaco-editor'
loader.config({ monaco })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders />
  </StrictMode>
)
