<template>
  <el-container class="emp-layout">
    <el-aside :width="collapsed ? '0px' : '220px'" class="emp-aside">
      <div class="logo">排班系统 · 员工端</div>
      <el-menu
        :default-active="$route.path"
        @select="handleMenuSelect"
      >
        <el-menu-item index="/employee/schedule">
          <el-icon><Calendar /></el-icon>
          <span>我的班表</span>
        </el-menu-item>
        <!-- 管理员/店长预览模式：只显示班表查看，隐藏员工操作功能 -->
        <template v-if="authStore.role === 'EMPLOYEE'">
          <el-menu-item index="/employee/leave">
            <el-icon><Document /></el-icon>
            <span>请假申请</span>
          </el-menu-item>
          <el-menu-item index="/employee/swap">
            <el-icon><Switch /></el-icon>
            <span>换班申请</span>
          </el-menu-item>
        </template>
        <!-- 预览提示 -->
        <div v-if="authStore.role !== 'EMPLOYEE'" class="preview-tip">预览模式：仅供查看班表</div>
      </el-menu>
      <!-- 管理员/店长预览模式：返回管理端入口（普通员工不显示），样式与管理端底部入口一致 -->
      <div
        v-if="authStore.role !== 'EMPLOYEE'"
        class="emp-aside-footer"
        @click="router.push('/dashboard')"
      >
        <el-icon><ArrowLeft /></el-icon>
        <span>返回管理端</span>
      </div>
    </el-aside>
    <!-- 书签样式按钮：常驻左边缘；展开时显示「收起」，收起时显示「展开」 -->
    <button class="sidebar-tab" :class="{ expanded: !collapsed }" :title="collapsed ? '展开侧边栏' : '收起侧边栏'" @click="collapsed = !collapsed">
      <el-icon :size="16"><Expand v-if="collapsed" /><Fold v-else /></el-icon>
      <span>{{ collapsed ? '展开' : '收起' }}</span>
    </button>
    <el-container>
      <el-header class="emp-header">
        <div class="header-left">
          <div class="header-title">{{ $route.meta.title }}</div>
        </div>
        <div class="u-row u-gap-6">
          <NotificationsPanel
            v-if="authStore.role === 'EMPLOYEE'"
            view-all-path="/employee/notifications"
          />
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
      <el-main class="emp-content" :class="{ collapsed }">
        <router-view :key="$route.path" />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { onMounted, ref, watch } from 'vue'
import { ArrowDown, ArrowLeft, Calendar, Document, Expand, Fold, Switch } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import ChangePasswordDialog from '../components/ChangePasswordDialog.vue'
import NotificationsPanel from '../components/NotificationsPanel.vue'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
// 侧边栏收起状态：与员工端独立持久化，刷新后保持
const collapsed = ref(localStorage.getItem('emp-sidebar-collapsed') === '1')
watch(collapsed, v => localStorage.setItem('emp-sidebar-collapsed', v ? '1' : '0'))

// 菜单切换（预览员工参数由「我的班表」页面自行管理）
function handleMenuSelect(index) {
  router.push(index)
}

onMounted(async () => {
  const role = authStore.role || localStorage.getItem('shift_role') || ''
  // 管理员预览模式：仅允许查看班表，其他员工功能页重定向回班表
  if (role !== 'EMPLOYEE' && !['/employee/schedule'].includes(route.path)) {
    router.replace({ path: '/employee/schedule', query: route.query })
  }
  if (authStore.isAuthenticated && !authStore.user) {
    try {
      // P3-26: 用户加载失败跳转登录
      await authStore.fetchCurrentUser()
    } catch (e) {
      console.error('获取当前用户失败', e)
      // 仅会话失效（401）才登出并跳登录；网络错误保留会话
      if (e?.status === 401) {
        authStore.logout()
        router.push('/login')
      }
    }
  }
})

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
.emp-layout {
  height: 100%;
}
.emp-aside {
  background-color: var(--app-brand);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition: width 0.25s ease;
}
.logo {
  height: 60px;
  line-height: 60px;
  text-align: center;
  color: var(--el-color-white);
  font-size: var(--app-font-lg);
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
.emp-aside :deep(.el-menu) {
  --el-menu-bg-color: var(--app-sidebar-bg);
  --el-menu-text-color: var(--app-sidebar-text);
  --el-menu-active-color: var(--app-sidebar-text-active);
  --el-menu-hover-bg-color: var(--app-menu-hover-bg);
  --el-menu-item-hover-fill: var(--app-menu-hover-bg);
  border-right: none;
  flex: 1;
}
/* 选中菜单项加深背景，突出当前页面 */
.emp-aside :deep(.el-menu-item.is-active),
.emp-aside :deep(.el-menu-item.is-active:hover) {
  background-color: var(--app-brand-active);
}
.emp-aside-footer {
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
.emp-aside-footer:hover {
  color: var(--el-color-white);
  background-color: rgba(255, 255, 255, 0.06);
}
.preview-tip {
  padding: var(--app-space-3) var(--app-space-5);
  color: var(--el-color-success);
  font-size: var(--app-font-sm);
  border-top: 1px solid rgba(255,255,255,0.1);
  margin-top: var(--app-space-2);
  opacity: 0.85;
}
.emp-header {
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
.emp-content {
  overflow-y: auto;
}
/* 收起侧边栏后页面内容水平居中 */
.emp-content.collapsed :deep(> *) {
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}
/* 表格不再限高:内容多长表格多长,整页滚动,避免表格内嵌套滚动条 */
</style>