import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { ThemeProvider } from './theme/ThemeProvider'
import { bootstrapAuthFromUrl } from './auth/bootstrap'
import './index.css'

// Captura access_token/app_origin da URL antes de o React montar pra que
// o primeiro request (ex.: /me em qualquer componente) já saia com o header
// correto.
bootstrapAuthFromUrl()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ThemeProvider>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ThemeProvider>
  </React.StrictMode>,
)
