<template>
  <el-container class="main-layout">
    <el-aside :width="collapsed ? '0px' : '220px'" class="main-aside">
      <div class="logo">排班系统</div>
      <el-menu
        :default-active="$route.path"
        @select="handleMenuSelect"
      >
        <!-- 长沙滚滚（store_id=2）：精简菜单 排班明细 + 审批 -->
        <template v-if="isCsgg && authStore.role !== 'EMPLOYEE'">
          <el-menu-item index="/schedules/detail">
            <el-icon><Calendar /></el-icon>
            <span>排班明细</span>
          </el-menu-item>
          <el-menu-item v-if="isCsggManager" index="/leave-review">
            <el-icon><Stamp /></el-icon>
            <span>请假审批</span>
          </el-menu-item>
          <el-menu-item v-if="isCsggManager" index="/swap-review">
            <el-icon><Switch /></el-icon>
            <span>换班审批</span>
          </el-menu-item>
          <el-menu-item index="/notifications">
            <el-icon><Bell /></el-icon>
            <span>通知消息</span>
          </el-menu-item>
        </template>
        <!-- 其他门店：完整菜单 -->
        <template v-else-if="authStore.role !== 'EMPLOYEE'">
          <el-menu-item index="/dashboard">
            <el-icon><DataBoard /></el-icon>
            <span>首页概览</span>
          </el-menu-item>
          <el-sub-menu index="basic">
            <template #title><span>基础数据</span></template>
            <el-menu-item index="/employees">员工管理</el-menu-item>
            <el-menu-item index="/rules">规则配置</el-menu-item>
            <el-menu-item v-if="authStore.username === SUPER_ADMIN_USERNAME" index="/date-parameters">日期参数</el-menu-item>
          </el-sub-menu>
          <!-- 门店级运营配置:仅店长可见,admin 不显示 -->
          <el-sub-menu v-if="authStore.role === 'STORE_MANAGER'" index="store-ops">
            <template #title><span>店长管理</span></template>
            <el-menu-item index="/workstations">工作站管理</el-menu-item>
            <el-menu-item index="/shift-templates">班次管理</el-menu-item>
            <el-menu-item index="/staffing-requirements">人数需求</el-menu-item>
            <el-menu-item index="/skill-matrix">技能等级</el-menu-item>
          </el-sub-menu>
          <el-sub-menu index="schedule">
            <template #title><span>排班管理</span></template>
            <el-menu-item index="/schedules/generate">一键排班</el-menu-item>
            <el-menu-item index="/schedules/view">排班查看</el-menu-item>
            <el-menu-item index="/reports">排班报表</el-menu-item>
            <el-menu-item v-if="authStore.username === SUPER_ADMIN_USERNAME" index="/audit-logs">审计日志</el-menu-item>
            <el-menu-item index="/preferences">偏好学习</el-menu-item>
            <el-menu-item v-if="authStore.username === STORE_MANAGER_USERNAME" index="/leave-review">请假审批</el-menu-item>
            <el-menu-item v-if="authStore.username === STORE_MANAGER_USERNAME" index="/swap-review">换班审批</el-menu-item>
          </el-sub-menu>
        </template>
      </el-menu>
      <!-- 员工端入口固定在 sidebar 最底部，与菜单视觉分隔 -->
      <div
        v-if="authStore.role !== 'EMPLOYEE'"
        class="aside-footer"
        @click="handleMenuSelect('/employee/schedule')"
      >
        <el-icon><Calendar /></el-icon>
        <span>员工端（我的班表）</span>
      </div>
    </el-aside>
    <!-- 书签样式按钮：常驻左边缘；展开时显示「收起」，收起时显示「展开」 -->
    <button class="sidebar-tab" :class="{ expanded: !collapsed }" :title="collapsed ? '展开侧边栏' : '收起侧边栏'" @click="collapsed = !collapsed">
      <el-icon :size="16"><Expand v-if="collapsed" /><Fold v-else /></el-icon>
      <span>{{ collapsed ? '展开' : '收起' }}</span>
    </button>
    <el-container>
      <el-header class="main-header">
        <div class="header-left">
          <div class="header-title">{{ $route.meta.title }}</div>
        </div>
        <div class="u-row u-gap-6">
          <NotificationsPanel view-all-path="/notifications" />
          <el-dropdown @command="handleCommand">
            <span class="user-info">
              {{ authStore.user?.nickname || '未登录' }}
              <el-icon><ArrowDown /></el-icon>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="change-password">修改密码</el-dropdown-item>
                <el-dropdown-item command="logout" divided>退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
          <ChangePasswordDialog v-model="changePwdVisible" />
        </div>
      </el-header>
      <el-main class="main-content" :class="{ collapsed }">
        <!-- 只对页面内容加 key，避免整个布局（含 sidebar）重新挂载 -->
        <router-view :key="$route.path" />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowDown, Bell, Calendar, DataBoard, Expand, Fold, Stamp, Switch } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { SUPER_ADMIN_USERNAME, STORE_MANAGER_USERNAME } from '../constants/config'
import ChangePasswordDialog from '../components/ChangePasswordDialog.vue'
import NotificationsPanel from '../components/NotificationsPanel.vue'

const router = useRouter()
const authStore = useAuthStore()

// 长沙滚滚（store_id=2）：精简菜单；店长账号 A001 才能看到审批入口
const isCsgg = computed(() => authStore.storeId === '2')
const isCsggManager = computed(() => isCsgg.value && authStore.username === 'A001')

// 侧边栏收起状态：本地持久化，刷新后保持
const collapsed = ref(localStorage.getItem('sidebar-collapsed') === '1')
watch(collapsed, v => localStorage.setItem('sidebar-collapsed', v ? '1' : '0'))

onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    try {
      await authStore.fetchCurrentUser()
    } catch (e) {
      console.error('获取当前用户失败', e)
    }
  }
})

// 管理员进入员工端时默认选择第一个员工（昆明 E001 / 长沙 A002）预览
function handleMenuSelect(index) {
  if (index === '/employee/schedule') {
    router.push({ path: index, query: { employeeNo: isCsgg.value ? 'A002' : 'E001' } })
  } else {
    router.push(index)
  }
}

const changePwdVisible = ref(false)

async function handleCommand(command) {
  if (command === 'change-password') {
    changePwdVisible.value = true
    return
  }
  if (command === 'logout') {
    try {
      await ElMessageBox.confirm('确定要退出登录吗？', '提示', {
        confirmButtonText: '退出',
        cancelButtonText: '取消',
        type: 'warning'
      })
      authStore.logout()
      router.push('/login')
    } catch {}
  }
}
</script>

<style scoped>
.main-layout {
  height: 100%;
}
.main-aside {
  display: flex;
  flex-direction: column;
  background-color: var(--app-brand);
  overflow: hidden;
  transition: width 0.25s ease;
}
.logo {
  height: 60px;
  line-height: 60px;
  text-align: center;
  color: var(--el-color-white);
  font-size: var(--app-font-xl);
  font-weight: 600;
}
/* 书签样式按钮：常驻左边缘；收起时贴屏幕左缘，展开时贴 sidebar 右边界内侧（translate 保证贴边） */
.sidebar-tab {
  position: fixed;
  left: 0;
  top: 50%;
  transform: translateY(-50%);
  z-index: 100;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--app-space-4);
  padding: var(--app-space-6) 7px;
  background-color: var(--app-brand);
  color: rgba(255, 255, 255, 0.75);
  border: none;
  border-radius: 0 var(--app-radius-lg) var(--app-radius-lg) 0;
  cursor: pointer;
  font-size: var(--app-font-sm);
  box-shadow: 2px 0 8px rgba(0, 0, 0, 0.25);
  transition: color 0.2s, background-color 0.2s, left 0.25s ease, transform 0.25s ease;
  user-select: none;
}
.sidebar-tab span {
  writing-mode: vertical-lr;
  letter-spacing: 2px;
}
.sidebar-tab.expanded {
  left: 220px;
  transform: translate(-100%, -50%);
}
.sidebar-tab:hover {
  color: var(--el-color-white);
  background-color: var(--app-brand-hover);
}
/* 原先用 el-menu 的 background-color/text-color/active-text-color prop,
   那三个 prop 已废弃且要走 TinyColor 派生,无法消费 token,故改为直接给变量 */
.main-aside :deep(.el-menu) {
  --el-menu-bg-color: var(--app-sidebar-bg);
  --el-menu-text-color: var(--app-sidebar-text);
  --el-menu-active-color: var(--app-sidebar-text-active);
  --el-menu-hover-bg-color: var(--app-menu-hover-bg);
  --el-menu-item-hover-fill: var(--app-menu-hover-bg);
  flex: 1;
  overflow-y: auto;
  border-right: none;
}
/* 选中菜单项加深背景，突出当前页面 */
.main-aside :deep(.el-menu-item.is-active),
.main-aside :deep(.el-menu-item.is-active:hover) {
  background-color: var(--app-brand-active);
}
.aside-footer {
  display: flex;
  align-items: center;
  gap: var(--app-space-4);
  padding: 14px 20px;
  color: rgba(255, 255, 255, 0.65);
  cursor: pointer;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  transition: color 0.2s, background-color 0.2s;
  user-select: none;
}
.aside-footer:hover {
  color: var(--el-color-white);
  background-color: rgba(255, 255, 255, 0.06);
}
.main-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: var(--el-bg-color);
  border-bottom: 1px solid var(--el-border-color-light);
}
.header-left {
  display: flex;
  align-items: center;
  gap: var(--app-space-5);
}
.collapse-btn {
  cursor: pointer;
  padding: var(--app-space-3);
  border-radius: var(--app-radius-sm);
  color: var(--el-text-color-regular);
  transition: background-color 0.2s, color 0.2s;
}
.collapse-btn:hover {
  background-color: rgba(0, 0, 0, 0.06);
  color: var(--el-color-primary);
}
.header-title {
  font-size: var(--app-font-lg);
  font-weight: 600;
}
.user-info {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: var(--app-space-2);
}
.main-content {
  overflow-y: auto;
}
/* 收起侧边栏后页面内容水平居中（1400px 内居中，超出贴边） */
.main-content.collapsed :deep(> *) {
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}
.main-content :deep(.el-table__body-wrapper) {
  max-height: calc(100vh - 240px);
  overflow-y: auto;
}
</style>