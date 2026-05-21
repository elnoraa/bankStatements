import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: process.env.VITE_PROXY_TARGET || 'http://localhost:5213',
        changeOrigin: true,
      },
      '/hubs': {
        target: process.env.VITE_PROXY_TARGET || 'http://localhost:5213',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
