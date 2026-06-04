import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    // Vite default é 'assets' — renomeamos pra 'mvp-assets' porque o host
    // em deploy frequentemente tem um /assets próprio (de outra app/feature),
    // e o name collision derruba o load do bundle do MVP. Long-term cache
    // do nginx é por extensão (.js|.css|...), então não precisa ajustar.
    assetsDir: 'mvp-assets',
    // Bundle inicial estava em ~2 MB (gzip 600 KB). Particionamos por
    // estabilidade de update + peso: vendor que muda raramente fica
    // separado pra cache hit alto entre deploys, e bibliotecas pesadas
    // (BlockNote, markdown) ficam isoladas pra não inflar o chunk principal.
    rollupOptions: {
      output: {
        manualChunks: {
          // Stack base do React — atualiza com cadência de major release,
          // alto reuso entre rotas. Cache long-term agressivo.
          'vendor-react': ['react', 'react-dom', 'react-router'],
          // Editor rich-text usado só no AgentEditor (Profile/Worker steps).
          // ~500 KB minified — não justifica entrar no main.
          'vendor-blocknote': [
            '@blocknote/core',
            '@blocknote/mantine',
            '@blocknote/react',
          ],
          // Renderização markdown usada só no ReviewStep do AgentEditor.
          'vendor-markdown': ['react-markdown', 'remark-gfm'],
        },
      },
    },
    // Threshold em 1500 KB acomoda o vendor-blocknote (~1.45 MB real,
    // limite da lib em si — não é resolvível sem trocar de editor).
    // Warning continua ativo pra capturar inchamento dos outros chunks,
    // que devem ficar bem abaixo desse limite.
    chunkSizeWarningLimit: 1500,
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
