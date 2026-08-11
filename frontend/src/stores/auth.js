import { defineStore } from 'pinia'
import { login as loginApi, getCurrentUser } from '../api/auth'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem('shift_token') || '',
    user: null,
    store: null,
    role: localStorage.getItem('shift_role') || ''
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
      localStorage.setItem('shift_token', res.token)
      localStorage.setItem('shift_role', res.user.role || '')
      return res
    },
    async fetchCurrentUser() {
      try {
        this.user = await getCurrentUser()
        if (!this.user || !this.user.role) {
          throw new Error('用户信息无效')
        }
        this.role = this.user.role
        localStorage.setItem('shift_role', this.user.role || '')
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
      localStorage.removeItem('shift_token')
      localStorage.removeItem('shift_role')
    }
  }
})