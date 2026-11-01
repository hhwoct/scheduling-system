import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

// 环境变量优先级：.env / .env.local 文件 > 进程环境变量 > 代码默认值
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  // 默认 0.0.0.0：手机与电脑同一局域网时，用电脑 IP + 端口直接访问验收
  const host = env.VITE_HOST || process.env.VITE_HOST || '0.0.0.0'
  const port = Number(env.VITE_PORT || process.env.VITE_PORT || 5180)
  const apiTarget = env.VITE_API_TARGET || process.env.VITE_API_TARGET || 'http://127.0.0.1:5059'

  return {
    plugins: [vue()],
    server: {
      host,
      port,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true
        }
      }
    }
  }
})
