export const API_ROUTES = {
  AUTH: {
    LOGIN: '/auth/login',
    FORGOT_PASSWORD: '/auth/forgot-password',
    ME: '/auth/me'
  },
  EMPLOYEES: {
    BASE: '/employees',
    DETAIL: (id) => `/employees/${encodeURIComponent(id)}`,
    SKILLS: (id) => `/employees/${encodeURIComponent(id)}/skills`,
    STATUS: (id) => `/employees/${encodeURIComponent(id)}/status`
  },
  WORKSTATIONS: {
    BASE: '/workstations'
  },
  SHIFT_TEMPLATES: {
    BASE: '/shift-templates'
  },
  RULES: {
    BASE: '/rules'
  },
  SCHEDULES: {
    BASE: '/schedules',
    GENERATE: '/schedules/generate',
    VIEW: '/schedules/view',
    DETAIL: (planId) => `/schedules/${encodeURIComponent(planId)}`,
    ISSUES: (planId) => `/schedules/${encodeURIComponent(planId)}/issues`,
    WEEK_VIEW: (planId) => `/schedules/${encodeURIComponent(planId)}/week-view`,
    MONTH_VIEW: (planId) => `/schedules/${encodeURIComponent(planId)}/month-view`,
    DAILY_VIEW: (planId) => `/schedules/${encodeURIComponent(planId)}/daily-view`,
    SUMMARY: (planId) => `/schedules/${encodeURIComponent(planId)}/summary`,
    RATIONALITY: (planId) => `/schedules/${encodeURIComponent(planId)}/rationality`,
    ADJUST: (planId) => `/schedules/${encodeURIComponent(planId)}/adjust`,
    DAY_STATUS: (planId) => `/schedules/${encodeURIComponent(planId)}/day-status`,
    SLOT_STATUS: (planId) => `/schedules/${encodeURIComponent(planId)}/slot-status`,
    PUBLISH: (planId) => `/schedules/${encodeURIComponent(planId)}/publish`,
    UNPUBLISH: (planId) => `/schedules/${encodeURIComponent(planId)}/unpublish`,
    ADD_SLOT: (planId) => `/schedules/${encodeURIComponent(planId)}/add-slot`,
    REMOVE_SLOT: (planId) => `/schedules/${encodeURIComponent(planId)}/remove-slot`,
    ADD_CANDIDATES: (planId) => `/schedules/${encodeURIComponent(planId)}/add-candidates`
  },
  LEAVE: {
    BASE: '/leave-requests',
    MINE: '/leave-requests/mine',
    REVIEW: '/leave-requests/review',
    REVIEW_DETAIL: (id) => `/leave-requests/${encodeURIComponent(id)}/review`,
    EARLY_RETURN: (id) => `/leave-requests/${encodeURIComponent(id)}/early-return`
  },
  SWAP: {
    BASE: '/shift-swaps',
    MINE: '/shift-swaps/mine',
    CANDIDATES: '/shift-swaps/candidates',
    REVIEW: '/shift-swaps/review',
    REVIEW_DETAIL: (id) => `/shift-swaps/${encodeURIComponent(id)}/review`
  },
  EMPLOYEE: {
    SWAP_PLANS: '/employee/swap-plans'
  },
  NOTIFICATIONS: {
    BASE: '/notifications',
    UNREAD_COUNT: '/notifications/unread-count',
    READ_ALL: '/notifications/read-all',
    READ: (id) => `/notifications/${encodeURIComponent(id)}/read`
  },
  AUDIT_LOGS: {
    BASE: '/audit-logs'
  },
  DASHBOARD: {
    STATS: '/dashboard/stats'
  },
  STORE: {
    CURRENT: '/stores/current'
  }
}