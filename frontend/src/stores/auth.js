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
      this.token = res.token
      this.user = res.user
      this.role = res.user.role
      localStorage.setItem('shift_token', res.token)
      localStorage.setItem('shift_role', res.user.role || '')
      return res
    },
    async fetchCurrentUser() {
      this.user = await getCurrentUser()
      this.role = this.user.role
      localStorage.setItem('shift_role', this.user.role || '')
      return this.user
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
