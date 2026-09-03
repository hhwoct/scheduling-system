import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    // 0.0.0.0：手机与电脑同一局域网时，用电脑 IP + 端口直接访问验收
    host: process.env.VITE_HOST || '0.0.0.0',
    port: 5180,
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET || 'http://localhost:5059',
        changeOrigin: true
      }
    }
  }
})
