export const API_ROUTES = {
  EMPLOYEES: {
    BASE: '/employees',
    DETAIL: (id) => `/employees/${encodeURIComponent(id)}`
  },
  EMPLOYEE: {
    MY_SCHEDULE: '/employee/my-schedule'
  },
  LEAVE: {
    BASE: '/leave-requests',
    MINE: '/leave-requests/mine',
    EARLY_RETURN: (id) => `/leave-requests/${encodeURIComponent(id)}/early-return`
  },
  SWAP: {
    BASE: '/shift-swaps',
    MINE: '/shift-swaps/mine',
    CANDIDATES: '/shift-swaps/candidates',
    PLANS: '/employee/swap-plans'
  },
  NOTIFICATIONS: {
    BASE: '/notifications',
    UNREAD_COUNT: '/notifications/unread-count',
    READ: (id) => `/notifications/${encodeURIComponent(id)}/read`,
    READ_ALL: '/notifications/read-all'
  },
  SCHEDULES: {
    BASE: '/schedules',
    WEEK_VIEW: (id) => `/schedules/${encodeURIComponent(id)}/week-view`,
    MONTH_VIEW: (id) => `/schedules/${encodeURIComponent(id)}/month-view`,
    DAILY_VIEW: (id) => `/schedules/${encodeURIComponent(id)}/daily-view`,
    ISSUES: (id) => `/schedules/${encodeURIComponent(id)}/issues`,
    PUBLISH: (id) => `/schedules/${encodeURIComponent(id)}/publish`,
    UNPUBLISH: (id) => `/schedules/${encodeURIComponent(id)}/unpublish`
  },
  REVIEWS: {
    LEAVE_LIST: '/leave-requests/review',
    LEAVE_REVIEW: (id) => `/leave-requests/${encodeURIComponent(id)}/review`,
    SWAP_LIST: '/shift-swaps/review',
    SWAP_REVIEW: (id) => `/shift-swaps/${encodeURIComponent(id)}/review`
  },
  AUTH: {
    LOGIN: '/auth/login',
    FORGOT_PASSWORD: '/auth/forgot-password',
    CHANGE_PASSWORD: '/auth/change-password',
    ME: '/auth/me'
  },
  STORE: {
    CURRENT: '/stores/current',
    ALL: '/stores'
  },
  RULES: {
    BASE: '/rules',
    DETAIL: (id) => `/rules/${encodeURIComponent(id)}`
  }
}
