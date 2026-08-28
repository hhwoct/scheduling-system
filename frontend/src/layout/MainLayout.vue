<template>
  <el-container class="main-layout">
    <el-aside :width="collapsed ? '0px' : '220px'" class="main-aside">
      <div class="logo">排班系统</div>
      <el-menu
        :default-active="$route.path"
        background-color="#0f3460"
        text-color="rgba(255,255,255,0.65)"
        active-text-color="#ffffff"
        @select="handleMenuSelect"
      >
        <el-menu-item v-if="authStore.role !== 'EMPLOYEE'" index="/dashboard">
          <el-icon><DataBoard /></el-icon>
          <span>首页概览</span>
        </el-menu-item>
        <el-sub-menu v-if="authStore.role !== 'EMPLOYEE'" index="basic">
          <template #title><span>基础数据</span></template>
          <el-menu-item index="/employees">员工管理</el-menu-item>
          <el-menu-item index="/workstations">工作站管理</el-menu-item>
          <el-menu-item index="/shift-templates">班次管理</el-menu-item>
          <el-menu-item index="/rules">规则配置</el-menu-item>
          <el-menu-item index="/peak-hours">高峰时段</el-menu-item>
          <el-menu-item index="/staffing-requirements">人数需求</el-menu-item>
          <el-menu-item index="/date-parameters">日期参数</el-menu-item>
          <el-menu-item index="/skill-matrix">技能等级</el-menu-item>
        </el-sub-menu>
        <el-sub-menu v-if="authStore.role !== 'EMPLOYEE'" index="schedule">
          <template #title><span>排班管理</span></template>
          <el-menu-item index="/schedules/generate">一键排班</el-menu-item>
          <el-menu-item index="/schedules/view">排班查看</el-menu-item>
          <el-menu-item index="/reports">排班报表</el-menu-item>
          <el-menu-item v-if="authStore.username === SUPER_ADMIN_USERNAME" index="/audit-logs">审计日志</el-menu-item>
          <el-menu-item index="/preferences">偏好学习</el-menu-item>
          <el-menu-item index="/leave-review">请假审批</el-menu-item>
          <el-menu-item index="/swap-review">换班审批</el-menu-item>
          <el-menu-item index="/notifications">通知消息</el-menu-item>
        </el-sub-menu>
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
        <div style="display: flex; align-items: center; gap: 16px">
          <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99" style="cursor: pointer" @click="$router.push('/notifications')">
            <el-icon :size="20"><Bell /></el-icon>
          </el-badge>
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
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowDown, Bell, Calendar, DataBoard, Expand, Fold } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { getUnreadCount } from '../api/notifications'
import { SUPER_ADMIN_USERNAME } from '../constants/config'
import ChangePasswordDialog from '../components/ChangePasswordDialog.vue'

const router = useRouter()
const authStore = useAuthStore()
const unreadCount = ref(0)

// 侧边栏收起状态：本地持久化，刷新后保持
const collapsed = ref(localStorage.getItem('sidebar-collapsed') === '1')
watch(collapsed, v => localStorage.setItem('sidebar-collapsed', v ? '1' : '0'))

// 未读通知数：传当前用户工号，失败记录日志，focus 与路由变化时重取
async function refreshUnreadCount() {
  try {
    const data = await getUnreadCount(authStore.user?.employeeNo || undefined)
    unreadCount.value = data?.count ?? 0
  } catch (e) {
    console.error('获取未读通知数失败', e)
  }
}

onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    try {
      await authStore.fetchCurrentUser()
    } catch (e) {
      console.error('获取当前用户失败', e)
    }
  }
  if (authStore.isAuthenticated) {
    refreshUnreadCount()
  }
  window.addEventListener('focus', refreshUnreadCount)
  // 通知页标记已读/全部已读后刷新 header 红点
  window.addEventListener('notifications-changed', refreshUnreadCount)
})

onBeforeUnmount(() => {
  window.removeEventListener('focus', refreshUnreadCount)
  window.removeEventListener('notifications-changed', refreshUnreadCount)
})

// 访问通知页等路由变化后刷新未读数
watch(() => router.currentRoute.value.path, refreshUnreadCount)

// 管理员进入员工端时默认选择第一个员工（E001）预览
function handleMenuSelect(index) {
  if (index === '/employee/schedule') {
    router.push({ path: index, query: { employeeNo: 'E001' } })
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
  background-color: #0f3460;
  overflow: hidden;
  transition: width 0.25s ease;
}
.logo {
  height: 60px;
  line-height: 60px;
  text-align: center;
  color: #fff;
  font-size: 18px;
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
  gap: 8px;
  padding: 16px 7px;
  background-color: #0f3460;
  color: rgba(255, 255, 255, 0.75);
  border: none;
  border-radius: 0 10px 10px 0;
  cursor: pointer;
  font-size: 12px;
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
  color: #fff;
  background-color: #12395c;
}
.main-aside :deep(.el-menu) {
  flex: 1;
  overflow-y: auto;
  border-right: none;
}
/* 选中菜单项加深背景，突出当前页面 */
.main-aside :deep(.el-menu-item.is-active),
.main-aside :deep(.el-menu-item.is-active:hover) {
  background-color: #0a2540;
}
.aside-footer {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 14px 20px;
  color: rgba(255, 255, 255, 0.65);
  cursor: pointer;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  transition: color 0.2s, background-color 0.2s;
  user-select: none;
}
.aside-footer:hover {
  color: #fff;
  background-color: rgba(255, 255, 255, 0.06);
}
.main-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #fff;
  border-bottom: 1px solid #e4e7ed;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}
.collapse-btn {
  cursor: pointer;
  padding: 6px;
  border-radius: 4px;
  color: #606266;
  transition: background-color 0.2s, color 0.2s;
}
.collapse-btn:hover {
  background-color: rgba(0, 0, 0, 0.06);
  color: #409eff;
}
.header-title {
  font-size: 16px;
  font-weight: 600;
}
.user-info {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 4px;
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