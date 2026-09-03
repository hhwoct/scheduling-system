import { defineStore } from 'pinia'
import { getLeaveReviews, getSwapReviews } from '../api/reviews'
import { useAuthStore } from './auth'

// 店长端「审批」Tab 的待办角标（员工请假 + 换班待审批数，不含店长自己的申请）
export const useReviewStore = defineStore('reviews', {
  state: () => ({
    leavePending: 0,
    swapPending: 0
  }),
  getters: {
    pendingTotal: (state) => state.leavePending + state.swapPending
  },
  actions: {
    async fetchCounts() {
      try {
        const [leave, swap] = await Promise.all([
          getLeaveReviews('PENDING'),
          getSwapReviews('PENDING')
        ])
        const auth = useAuthStore()
        const mine = auth.username || ''
        this.leavePending = (leave || []).filter((x) => x.employee?.employeeNo !== mine).length
        this.swapPending = (swap || []).filter((x) => x.requester?.employeeNo !== mine).length
      } catch (e) {
        // 静默失败：角标不因瞬时网络问题清零
      }
    }
  }
})
