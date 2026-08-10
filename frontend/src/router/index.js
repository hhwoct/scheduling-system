import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/LoginView.vue'),
    meta: { title: '登录' }
  },
  {
    path: '/employee',
    component: () => import('../layout/EmployeeLayout.vue'),
    redirect: '/employee/schedule',
    meta: { employee: true },
    children: [
      {
        path: 'schedule',
        name: 'EmployeeSchedule',
        component: () => import('../views/EmployeeScheduleView.vue'),
        meta: { title: '我的班表', employee: true }
      },
      {
        path: 'leave',
        name: 'EmployeeLeave',
        component: () => import('../views/EmployeeLeaveView.vue'),
        meta: { title: '请假申请', employee: true }
      },
      {
        path: 'swap',
        name: 'EmployeeSwap',
        component: () => import('../views/EmployeeSwapView.vue'),
        meta: { title: '换班申请', employee: true }
      },
      {
        path: 'notifications',
        name: 'EmployeeNotifications',
        component: () => import('../views/NotificationView.vue'),
        meta: { title: '通知消息', employee: true }
      }
    ]
  },
  {
    path: '/',
    component: () => import('../layout/MainLayout.vue'),
    redirect: '/dashboard',
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('../views/DashboardView.vue'),
        meta: { title: '首页概览' }
      },
      {
        path: 'employees',
        name: 'Employees',
        component: () => import('../views/EmployeeView.vue'),
        meta: { title: '员工管理' }
      },
      {
        path: 'workstations',
        name: 'Workstations',
        component: () => import('../views/WorkstationView.vue'),
        meta: { title: '工作站管理' }
      },
      {
        path: 'shift-templates',
        name: 'ShiftTemplates',
        component: () => import('../views/ShiftTemplateView.vue'),
        meta: { title: '班次管理' }
      },
      {
        path: 'rules',
        name: 'Rules',
        component: () => import('../views/RuleConfigView.vue'),
        meta: { title: '规则配置' }
      },
      {
        path: 'schedules/generate',
        name: 'ScheduleGenerate',
        component: () => import('../views/ScheduleGenerateView.vue'),
        meta: { title: '一键排班' }
      },
      {
        path: 'schedules/view',
        name: 'ScheduleView',
        component: () => import('../views/ScheduleView.vue'),
        meta: { title: '排班查看' }
      },
      {
        path: 'reports',
        name: 'Reports',
        component: () => import('../views/ReportView.vue'),
        meta: { title: '排班报表' }
      },
      {
        path: 'audit-logs',
        name: 'AuditLogs',
        component: () => import('../views/AuditLogView.vue'),
        meta: { title: '审计日志' }
      },
      {
        path: 'leave-review',
        name: 'LeaveReview',
        component: () => import('../views/LeaveReviewView.vue'),
        meta: { title: '请假审批' }
      },
      {
        path: 'swap-review',
        name: 'SwapReview',
        component: () => import('../views/SwapReviewView.vue'),
        meta: { title: '换班审批' }
      },
      {
        path: 'notifications',
        name: 'Notifications',
        component: () => import('../views/NotificationView.vue'),
        meta: { title: '通知消息' }
      }
    ]
  },
  {
    path: '/:pathMatch(.*)*',
    redirect: '/'
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to) => {
  const token = localStorage.getItem('shift_token')
  const role = localStorage.getItem('shift_role') || ''

  // 未登录 -> 登录页
  if (to.path !== '/login' && !token) {
    return { path: '/login' }
  }

  // 已登录访问登录页 -> 按角色跳
  if (to.path === '/login' && token) {
    return role === 'EMPLOYEE' ? { path: '/employee' } : { path: '/' }
  }

  // 员工只允许访问员工端
  if (token && role === 'EMPLOYEE' && to.path !== '/employee' && !to.path.startsWith('/employee/')) {
    return { path: '/employee' }
  }

  // 管理/店长可同时访问员工端与管理端（通过 /employee/ 前缀进入员工端）
  if (token && (role === 'STORE_MANAGER' || role === 'SYSTEM_ADMIN') && to.path === '/employee') {
    return { path: '/employee/schedule' }
  }

  document.title = to.meta.title ? `${to.meta.title} - 排班系统` : '排班系统'
})

export default router
