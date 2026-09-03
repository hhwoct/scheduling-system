import { defineStore } from 'pinia'
import { getUnreadCount } from '../api/notifications'

const POLL_INTERVAL_MS = 45000

export const useNotificationStore = defineStore('notifications', {
  state: () => ({
    unreadCount: 0,
    baseTitle: '排班系统',
    timer: null
  }),
  actions: {
    setBaseTitle(title) {
      this.baseTitle = title || '排班系统'
      this.applyTitle()
    },
    applyTitle() {
      document.title =
        this.unreadCount > 0
          ? `(${this.unreadCount}条未读) ${this.baseTitle}`
          : this.baseTitle
    },
    async fetchUnread() {
      try {
        const data = await getUnreadCount()
        this.unreadCount = Number(data?.count) || 0
        this.applyTitle()
      } catch (e) {
        // 静默失败：未读角标不因瞬时网络问题清零
      }
    },
    // 前台轮询：页面不可见时跳过（恢复可见时由 visibilitychange 立即补拉）
    startPolling(intervalMs = POLL_INTERVAL_MS) {
      this.stopPolling()
      this.fetchUnread()
      this.timer = setInterval(() => {
        if (document.visibilityState === 'visible') {
          this.fetchUnread()
        }
      }, intervalMs)
    },
    stopPolling() {
      if (this.timer) {
        clearInterval(this.timer)
        this.timer = null
      }
    }
  }
})
