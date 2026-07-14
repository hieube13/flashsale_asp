import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/ticket':  { target: 'http://localhost:5080', changeOrigin: true },
      '/order':   { target: 'http://localhost:5080', changeOrigin: true },
      '/api':     { target: 'http://localhost:5080', changeOrigin: true },
      '/hello':   { target: 'http://localhost:5080', changeOrigin: true },
      '/metrics':  { target: 'http://localhost:5080', changeOrigin: true },
      '/payment':  { target: 'http://localhost:5080', changeOrigin: true },
      '/health':   { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
})
