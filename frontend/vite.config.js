import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

// 环境变量优先级：.env / .env.local 文件 > 进程环境变量 > 代码默认值
// 说明：原先只读 process.env，导致 .env 里的 VITE_API_TARGET 不生效，这里改用 loadEnv 统一
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  const host = env.VITE_HOST || process.env.VITE_HOST || '127.0.0.1'
  const port = Number(env.VITE_PORT || process.env.VITE_PORT || 5173)
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
