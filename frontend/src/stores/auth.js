import { defineStore } from 'pinia'
import { login as loginApi, getCurrentUser } from '../api/auth'

// Token 短期有效期（分钟），与后端 Jwt:ExpireMinutes 保持一致
const TOKEN_TTL_MINUTES = 30
const STORAGE_KEY_TOKEN = 'shift_token'
const STORAGE_KEY_ROLE = 'shift_role'
const STORAGE_KEY_EXPIRES = 'shift_token_expires_at'

/**
 * 缓存 token 到 localStorage 并记录过期时间。
 * 每次登录/刷新时调用。
 */
function persistToken(token, role) {
  const expiresAt = Date.now() + TOKEN_TTL_MINUTES * 60 * 1000
  localStorage.setItem(STORAGE_KEY_TOKEN, token)
  localStorage.setItem(STORAGE_KEY_ROLE, role || '')
  localStorage.setItem(STORAGE_KEY_EXPIRES, String(expiresAt))
}

/**
 * 读取已缓存的 token；若已过期则清除并返回空。
 */
function loadToken() {
  const token = localStorage.getItem(STORAGE_KEY_TOKEN)
  if (!token) return ''
  const expiresAt = Number(localStorage.getItem(STORAGE_KEY_EXPIRES) || 0)
  if (expiresAt && Date.now() > expiresAt) {
    clearStoredAuth()
    return ''
  }
  return token
}

function clearStoredAuth() {
  localStorage.removeItem(STORAGE_KEY_TOKEN)
  localStorage.removeItem(STORAGE_KEY_ROLE)
  localStorage.removeItem(STORAGE_KEY_EXPIRES)
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: loadToken(),
    user: null,
    store: null,
    role: localStorage.getItem(STORAGE_KEY_ROLE) || ''
  }),
  getters: {
    isAuthenticated: (state) => !!state.token
  },
  actions: {
    async login(username, password) {
      const res = await loginApi({ username, password })
      // 安全加固：验证响应结构包含 token 和 user 才存储，否则视为无效响应
      if (!res || !res.token || !res.user || !res.user.role) {
        this.logout()
        throw new Error('登录响应格式异常，请稍后重试')
      }
      this.token = res.token
      this.user = res.user
      this.role = res.user.role
      persistToken(res.token, res.user.role || '')
      return res
    },
    async fetchCurrentUser() {
      try {
        this.user = await getCurrentUser()
        if (!this.user || !this.user.role) {
          throw new Error('用户信息无效')
        }
        this.role = this.user.role
        // 刷新 token 有效期（当前 token 仍有效，仅刷新过期时间）
        persistToken(this.token, this.user.role || '')
        return this.user
      } catch (e) {
        // 获取当前用户失败时清除旧状态，避免残留脏数据
        this.logout()
        throw e
      }
    },
    logout() {
      this.token = ''
      this.user = null
      this.store = null
      this.role = ''
      clearStoredAuth()
    }
  }
})