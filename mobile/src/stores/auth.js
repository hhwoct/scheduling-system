import { defineStore } from 'pinia'
import { login as loginApi, getCurrentUser } from '../api/auth'
import { SUPER_ADMIN_USERNAME } from '../constants/config'

// 与 PC 端共用同一套 localStorage 键：
// 未来若部署在同一域名下，两端登录态可互通；在各自 dev 端口下互不影响
const STORAGE_KEY_TOKEN = 'shift_token'
const STORAGE_KEY_ROLE = 'shift_role'
const STORAGE_KEY_USERNAME = 'shift_username'
const STORAGE_KEY_STORE_ID = 'shift_store_id'

function persistToken(token, role, username, storeId) {
  localStorage.setItem(STORAGE_KEY_TOKEN, token)
  localStorage.setItem(STORAGE_KEY_ROLE, role || '')
  localStorage.setItem(STORAGE_KEY_USERNAME, username || '')
  if (storeId != null) localStorage.setItem(STORAGE_KEY_STORE_ID, String(storeId))
}

function loadToken() {
  return localStorage.getItem(STORAGE_KEY_TOKEN) || ''
}

function clearStoredAuth() {
  localStorage.removeItem(STORAGE_KEY_TOKEN)
  localStorage.removeItem(STORAGE_KEY_ROLE)
  localStorage.removeItem(STORAGE_KEY_USERNAME)
  localStorage.removeItem(STORAGE_KEY_STORE_ID)
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: loadToken(),
    user: null,
    store: null,
    role: localStorage.getItem(STORAGE_KEY_ROLE) || '',
    username: localStorage.getItem(STORAGE_KEY_USERNAME) || '',
    storeId: localStorage.getItem(STORAGE_KEY_STORE_ID) || ''
  }),
  getters: {
    isAuthenticated: (state) => !!state.token,
    // 业务角色：店长账号（E001 等）在数据库中的角色也是 SYSTEM_ADMIN，
    // 按用户名区分（与后端 SuperAdminUsername/StoreManagerUsername 约定一致）：
    //   admin    = 系统管理员（手机端提示使用电脑端）
    //   manager  = 店长（其余 SYSTEM_ADMIN 账号视为店长）
    //   employee = 员工
    effectiveRole: (state) => {
      if (state.role === 'EMPLOYEE') return 'employee'
      if (state.role === 'STORE_MANAGER') return 'manager'
      if (state.role === 'SYSTEM_ADMIN') {
        return state.username === SUPER_ADMIN_USERNAME ? 'admin' : 'manager'
      }
      return ''
    }
  },
  actions: {
    async login(username, password) {
      const res = await loginApi({ username, password })
      // 验证响应结构包含 token 和 user 才存储，否则视为无效响应
      if (!res || !res.token || !res.user || !res.user.role) {
        this.logout()
        throw new Error('登录响应格式异常，请稍后重试')
      }
      this.token = res.token
      this.user = res.user
      this.role = res.user.role
      this.username = res.user.username || ''
      this.storeId = res.user.storeId != null ? String(res.user.storeId) : ''
      persistToken(res.token, res.user.role || '', res.user.username || '', this.storeId)
      return res
    },
    async fetchCurrentUser() {
      try {
        this.user = await getCurrentUser()
        if (!this.user || !this.user.role) {
          throw new Error('用户信息无效')
        }
        this.role = this.user.role
        this.username = this.user.username || ''
        this.storeId = this.user.storeId != null ? String(this.user.storeId) : ''
        return this.user
      } catch (e) {
        // 仅明确会话失效（401）才登出；瞬时网络错误/5xx 保留会话
        if (e?.status === 401) {
          this.logout()
        }
        throw e
      }
    },
    logout() {
      this.token = ''
      this.user = null
      this.store = null
      this.role = ''
      this.username = ''
      this.storeId = ''
      clearStoredAuth()
    }
  }
})

// 401 时清空 Pinia 内存态（request 拦截器清除 localStorage 后派发该事件）
window.addEventListener('auth:unauthorized', () => {
  const store = useAuthStore()
  if (store.token) store.logout()
})
