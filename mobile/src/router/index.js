import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useNotificationStore } from '../stores/notifications'

const routes = [
  {
    path: '/login',
    name: 'login',
    component: () => import('../views/login/LoginView.vue'),
    meta: { public: true, title: '登录' }
  },
  {
    path: '/forgot-password',
    name: 'forgotPassword',
    component: () => import('../views/login/ForgotPasswordView.vue'),
    meta: { public: true, title: '重置密码' }
  },
  // 超管端：员工管理 + 规则配置
  {
    path: '/admin',
    component: () => import('../views/admin/AdminLayout.vue'),
    children: [
      {
        path: '',
        redirect: '/admin/employees'
      },
      {
        path: 'employees',
        name: 'adminEmployees',
        component: () => import('../views/manager/EmployeeManageView.vue'),
        meta: { title: '员工管理' }
      },
      {
        path: 'employees/new',
        name: 'adminEmployeeNew',
        component: () => import('../views/manager/EmployeeFormView.vue'),
        meta: { title: '新增员工' }
      },
      {
        path: 'employees/edit',
        name: 'adminEmployeeEdit',
        component: () => import('../views/manager/EmployeeFormView.vue'),
        meta: { title: '编辑员工' }
      },
      {
        path: 'rules',
        name: 'adminRules',
        component: () => import('../views/admin/RulesView.vue'),
        meta: { title: '规则配置' }
      },
      {
        path: 'me',
        name: 'adminMe',
        component: () => import('../views/admin/AdminMeView.vue'),
        meta: { title: '我的' }
      },
      {
        path: 'me/password',
        name: 'adminChangePassword',
        component: () => import('../views/employee/ChangePasswordView.vue'),
        meta: { title: '修改密码' }
      }
    ]
  },
  // 员工端：底部 Tabbar 布局（班表已实现，其余页面逐步上线）
  {
    path: '/employee',
    component: () => import('../views/employee/EmployeeLayout.vue'),
    children: [
      {
        path: '',
        name: 'employeeSchedule',
        component: () => import('../views/employee/ScheduleView.vue'),
        meta: { title: '我的班表', home: 'employee' }
      },
      {
        path: 'leave',
        name: 'employeeLeaveSwap',
        component: () => import('../views/employee/LeaveSwapView.vue'),
        meta: {
          title: '换班&请假',
          createLeavePath: '/employee/leave/new',
          createSwapPath: '/employee/swap/new'
        }
      },
      {
        path: 'leave/new',
        name: 'employeeLeaveNew',
        component: () => import('../views/employee/LeaveFormView.vue'),
        meta: { title: '提交请假', backPath: '/employee/leave' }
      },
      {
        path: 'swap/new',
        name: 'employeeSwapNew',
        component: () => import('../views/employee/SwapFormView.vue'),
        meta: { title: '发起换班', backPath: '/employee/leave' }
      },
      {
        path: 'notifications',
        name: 'employeeNotifications',
        component: () => import('../views/employee/NotificationsView.vue'),
        meta: { title: '通知' }
      },
      {
        path: 'profile',
        name: 'employeeProfile',
        component: () => import('../views/employee/ProfileView.vue'),
        meta: { title: '我的' }
      },
      {
        path: 'profile/password',
        name: 'employeeChangePassword',
        component: () => import('../views/employee/ChangePasswordView.vue'),
        meta: { title: '修改密码' }
      }
    ]
  },
  // 店长端：底部 Tabbar 布局（排班已实现，审批/通讯录后续步骤上线）
  {
    path: '/manager',
    component: () => import('../views/manager/ManagerLayout.vue'),
    children: [
      {
        path: '',
        name: 'managerSchedule',
        component: () => import('../views/manager/ManagerScheduleView.vue'),
        meta: { title: '全店排班', home: 'manager' }
      },
      {
        path: 'my-schedule',
        name: 'managerMySchedule',
        component: () => import('../views/employee/ScheduleView.vue'),
        meta: { title: '我的班表' }
      },
      {
        path: 'reviews',
        name: 'managerReviews',
        component: () => import('../views/manager/ReviewView.vue'),
        meta: { title: '审批' }
      },
      {
        path: 'employees',
        name: 'managerEmployees',
        component: () => import('../views/manager/EmployeeManageView.vue'),
        meta: { title: '员工管理' }
      },
      {
        path: 'employees/new',
        name: 'managerEmployeeNew',
        component: () => import('../views/manager/EmployeeFormView.vue'),
        meta: { title: '新增员工' }
      },
      {
        path: 'employees/edit',
        name: 'managerEmployeeEdit',
        component: () => import('../views/manager/EmployeeFormView.vue'),
        meta: { title: '编辑员工' }
      },
      {
        path: 'notifications',
        name: 'managerNotifications',
        component: () => import('../views/employee/NotificationsView.vue'),
        meta: { title: '通知' }
      },
      {
        path: 'me',
        name: 'managerMe',
        component: () => import('../views/manager/ManagerMeView.vue'),
        meta: { title: '我的' }
      },
      {
        path: 'me/password',
        name: 'managerChangePassword',
        component: () => import('../views/employee/ChangePasswordView.vue'),
        meta: { title: '修改密码' }
      },
      {
        path: 'my-leave-swap',
        name: 'managerMyLeaveSwap',
        component: () => import('../views/employee/LeaveSwapView.vue'),
        meta: {
          title: '我的请假与换班',
          createLeavePath: '/manager/my-leave-swap/new-leave',
          createSwapPath: '/manager/my-leave-swap/new-swap'
        }
      },
      {
        path: 'my-leave-swap/new-leave',
        name: 'managerMyLeaveNew',
        component: () => import('../views/employee/LeaveFormView.vue'),
        meta: { title: '提交请假', backPath: '/manager/my-leave-swap' }
      },
      {
        path: 'my-leave-swap/new-swap',
        name: 'managerMySwapNew',
        component: () => import('../views/employee/SwapFormView.vue'),
        meta: { title: '发起换班', backPath: '/manager/my-leave-swap' }
      }
    ]
  },
  { path: '/', redirect: '/employee' },
  { path: '/:pathMatch(.*)*', redirect: '/employee' }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

function homeForRole(effectiveRole) {
  if (effectiveRole === 'employee') return '/employee'
  if (effectiveRole === 'manager') return '/manager'
  if (effectiveRole === 'admin') return '/admin'
  return '/login'
}

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public) {
    // 已登录访问登录/重置页：跳回对应首页
    if (auth.token && to.path !== '/forgot-password') {
      return homeForRole(auth.effectiveRole)
    }
    return true
  }

  if (!auth.token) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  // 已有 token 但缺少用户信息（如刷新页面后）：拉取 /auth/me
  if (!auth.user) {
    try {
      await auth.fetchCurrentUser()
    } catch (e) {
      return { path: '/login', query: { redirect: to.fullPath } }
    }
  }

  const effective = auth.effectiveRole

  // 系统管理员：手机端仅访问 /admin 体系（员工管理 + 规则配置）
  if (effective === 'admin') {
    if (!to.path.startsWith('/admin')) return '/admin'
    return true
  }

  // 员工区：仅员工（店长有自己的 /manager 体系：我的班表/请假与换班/改密等页面）
  if (to.path.startsWith('/employee') && effective !== 'employee') {
    return homeForRole(effective)
  }

  // 店长区：仅店长
  if (to.path.startsWith('/manager') && effective !== 'manager') {
    return homeForRole(effective)
  }

  return true
})

router.afterEach((to) => {
  const title = to.meta.title ? `${to.meta.title} · 排班系统` : '排班系统'
  document.title = title
  // 通知未读数会叠加到标题（如「(3条未读) 我的班表 · 排班系统」）
  useNotificationStore().setBaseTitle(title)
})

export default router
