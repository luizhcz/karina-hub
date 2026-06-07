import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Porta 5174 evita colisão com mvp/ (5173 em dev local) e com o frontend/
// legado. O proxy faz o backend Kestrel (rodando em 5189) responder em
// `/api/aihub/*` no mesmo origin do Vite, eliminando CORS no desenvolvimento.
// Em prod o frontend é servido atrás do mesmo reverse proxy que expõe o
// backend, então `baseUrl.ts` continua usando paths relativos.
export default defineConfig({
  plugins: [react()],
  build: {
    // Mesmo motivo do mvp/: o host de deploy pode ter um `/assets` próprio,
    // então prefixamos pra evitar colisão de nome de pasta no estático.
    assetsDir: 'analytics-assets',
    rollupOptions: {
      output: {
        manualChunks: {
          // React + router mudam por release — chunk separado pra cache long-term.
          'vendor-react': ['react', 'react-dom', 'react-router'],
          // Recharts arrasta D3-shape e ~150 KB. Isola pra não inchar o main.
          'vendor-charts': ['recharts'],
        },
      },
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
