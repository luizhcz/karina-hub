import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Durante a migração React → vanilla (Fase 0+), o entry HTML do React foi
// renomeado pra `react-fallback.html` pra liberar o nome `index.html` na
// pasta `public/` (vanilla Onboarding). nginx faz cascade:
//   $uri → $uri.html → $uri/index.html → /react-fallback.html
//
// Após Fase 5 (cleanup React), Vite + esse config saem; nginx serve só
// arquivos estáticos.
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: 'react-fallback.html',
    },
  },
  server: {
    port: 5174,
    proxy: {
      '/api': {
        target: 'http://localhost:5189',
        changeOrigin: true,
      },
    },
  },
})
