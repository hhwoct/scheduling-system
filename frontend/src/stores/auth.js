import { defineStore } from 'pinia'
import { ElMessage } from 'element-plus'
import { login as loginApi, getCurrentUser } from '../api/auth'
import router from '../router'

// 登录超时规则：15 分钟内没有任何操作（鼠标/键盘/触屏/滚动等）自动退出登录
const IDLE_TIMEOUT_MINUTES = 15
const IDLE_TIMEOUT_MS = IDLE_TIMEOUT_MINUTES * 60 * 1000
// 空闲检查周期：后台页签的定时器会被浏览器节流，恢复可见时会立即补检
const IDLE_CHECK_INTERVAL_MS = 10 * 1000

const STORAGE_KEY_TOKEN = 'shift_token'
const STORAGE_KEY_ROLE = 'shift_role'
// 旧版本遗留的「固定 30 分钟绝对过期」键，改为无操作超时后不再使用
const LEGACY_STORAGE_KEY_EXPIRES = 'shift_token_expires_at'

// 最近一次操作时间（仅内存态：刷新页面本身即视为一次操作，重新计时）
let lastActivityAt = Date.now()
let idleCheckTimer = null

const ACTIVITY_EVENTS = ['mousedown', 'mousemove', 'keydown', 'touchstart', 'pointerdown', 'scroll', 'wheel']

function touchActivity() {
  lastActivityAt = Date.now()
}

function autoLogout() {
  const store = useAuthStore()
  if (!store.token) return
  ElMessage.warning(`您已超过 ${IDLE_TIMEOUT_MINUTES} 分钟未操作，已自动退出登录`)
  store.logout()
  if (router.currentRoute.value.path !== '/login') {
    router.push('/login')
  }
}

function checkIdle() {
  // 页签隐藏时不判定（后台定时器被节流，恢复可见时统一补检）
  if (document.visibilityState === 'hidden') return
  const store = useAuthStore()
  if (!store.token) return
  if (Date.now() - lastActivityAt >= IDLE_TIMEOUT_MS) {
    autoLogout()
  }
}

function onVisibilityChange() {
  if (document.visibilityState !== 'visible') return
  if (Date.now() - lastActivityAt >= IDLE_TIMEOUT_MS) {
    autoLogout()
  } else {
    touchActivity()
  }
}

function startIdleWatcher() {
  if (idleCheckTimer) return
  ACTIVITY_EVENTS.forEach((name) =>
    window.addEventListener(name, touchActivity, { passive: true })
  )
  // 页签从后台恢复可见时立即校验：超时则直接退出，未超时视为回来操作、重新计时
  document.addEventListener('visibilitychange', onVisibilityChange)
  idleCheckTimer = setInterval(checkIdle, IDLE_CHECK_INTERVAL_MS)
}

function stopIdleWatcher() {
  if (idleCheckTimer) {
    clearInterval(idleCheckTimer)
    idleCheckTimer = null
  }
  ACTIVITY_EVENTS.forEach((name) =>
    window.removeEventListener(name, touchActivity)
  )
  document.removeEventListener('visibilitychange', onVisibilityChange)
}

/**
 * 缓存 token 到 localStorage。登录成功后调用。
 * 安全说明：localStorage 存在 XSS 窃取风险；根治方案需后端配合改用
 * HttpOnly + SameSite Cookie 并引入 CSRF 防护，前端现有流程保持不变。
 */
function persistToken(token, role) {
  localStorage.setItem(STORAGE_KEY_TOKEN, token)
  localStorage.setItem(STORAGE_KEY_ROLE, role || '')
  localStorage.removeItem(LEGACY_STORAGE_KEY_EXPIRES)
  touchActivity()
}

/**
 * 读取已缓存的 token；同时清理旧版本遗留的绝对过期时间键。
 */
function loadToken() {
  const token = localStorage.getItem(STORAGE_KEY_TOKEN)
  if (token) {
    localStorage.removeItem(LEGACY_STORAGE_KEY_EXPIRES)
  }
  return token || ''
}

function clearStoredAuth() {
  localStorage.removeItem(STORAGE_KEY_TOKEN)
  localStorage.removeItem(STORAGE_KEY_ROLE)
  localStorage.removeItem(LEGACY_STORAGE_KEY_EXPIRES)
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
      startIdleWatcher()
      return res
    },
    async fetchCurrentUser() {
      try {
        this.user = await getCurrentUser()
        if (!this.user || !this.user.role) {
          throw new Error('用户信息无效')
        }
        this.role = this.user.role
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
      clearStoredAuth()
      stopIdleWatcher()
    }
  }
})

// 模块加载即启动无操作监听：未登录时 checkIdle 直接跳过，无副作用
startIdleWatcher()

// 401 时清空 Pinia 内存态（request 拦截器清除 localStorage 后派发该事件）
window.addEventListener('auth:unauthorized', () => {
  const store = useAuthStore()
  if (store.token) store.logout()
})
