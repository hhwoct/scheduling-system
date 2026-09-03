<template>
  <div class="emp-layout">
    <!-- 固定深蓝毛玻璃侧边栏 -->
    <aside class="emp-aside" :class="{ collapsed }" :style="{ width: collapsed ? '0px' : 'var(--app-sidebar-width)' }">
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
    </aside>
    <div class="layout-body" :class="{ collapsed }">
      <!-- sticky 毛玻璃 header -->
      <header class="emp-header">
        <div class="header-left">
          <!-- 侧边栏收起/展开:header 左上角圆形图标按钮 -->
          <button
            class="sidebar-toggle"
            :title="collapsed ? '展开侧边栏' : '收起侧边栏'"
            aria-label="切换侧边栏"
            @click="collapsed = !collapsed"
          >
            <el-icon :size="18"><Expand v-if="collapsed" /><Fold v-else /></el-icon>
          </button>
          <div class="header-title">{{ $route.meta.title }}</div>
        </div>
        <div class="u-row u-gap-5">
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
      </header>
      <main class="emp-content" :class="{ collapsed }">
        <router-view v-slot="{ Component }">
          <transition name="page" mode="out-in">
            <component :is="Component" :key="$route.path" />
          </transition>
        </router-view>
      </main>
    </div>
  </div>
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

// 侧边栏收起状态：本地持久化，刷新后保持
const collapsed = ref(localStorage.getItem('sidebar-collapsed') === '1')
watch(collapsed, v => localStorage.setItem('sidebar-collapsed', v ? '1' : '0'))

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
  position: relative;
  height: 100%;
}

/* 极淡的品牌 aurora 底:给毛玻璃侧边栏提供可被模糊的层次 */
.emp-layout::before {
  content: '';
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background:
    radial-gradient(1100px 520px at 0% 0%, rgba(0, 46, 90, 0.14), transparent 62%),
    radial-gradient(900px 480px at 100% 100%, rgba(0, 122, 255, 0.1), transparent 60%);
}

/* ===== 侧边栏:固定 + 深蓝毛玻璃 ===== */
.emp-aside {
  position: fixed;
  left: 0;
  top: 0;
  bottom: 0;
  z-index: 110;
  display: flex;
  flex-direction: column;
  background-color: var(--app-sidebar-glass-bg);
  -webkit-backdrop-filter: var(--app-glass-blur);
  backdrop-filter: var(--app-glass-blur);
  border-right: 1px solid var(--app-sidebar-glass-border);
  box-shadow: 4px 0 24px rgba(0, 0, 0, 0.16);
  overflow: hidden;
  transition: width var(--app-duration-slow) var(--app-ease);
}

.emp-aside.collapsed {
  border-right: none;
  box-shadow: none;
}

.logo {
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #ffffff;
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.03em;
}

/* ===== 菜单:胶囊高亮 ===== */
.emp-aside :deep(.el-menu) {
  --el-menu-bg-color: transparent;
  --el-menu-text-color: var(--app-sidebar-text);
  --el-menu-active-color: var(--app-sidebar-text-active);
  --el-menu-hover-bg-color: transparent;
  --el-menu-item-hover-fill: transparent;
  border-right: none;
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 8px;
}

.emp-aside :deep(.el-menu-item) {
  height: 40px;
  line-height: 40px;
  border-radius: var(--app-radius-md);
  margin: 2px 0;
  padding: 0 12px !important;
  transition: background-color var(--app-duration-fast) ease, color var(--app-duration-fast) ease;
}

.emp-aside :deep(.el-menu-item:hover) {
  background-color: var(--app-sidebar-hover-bg);
}

.emp-aside :deep(.el-menu-item.is-active),
.emp-aside :deep(.el-menu-item.is-active:hover) {
  background-color: var(--app-sidebar-active-bg);
  color: var(--app-sidebar-text-active);
  font-weight: 600;
}

.emp-aside-footer {
  display: flex;
  align-items: center;
  gap: var(--app-space-4);
  margin: 8px;
  padding: 10px 12px;
  border-radius: var(--app-radius-md);
  color: rgba(255, 255, 255, 0.72);
  cursor: pointer;
  transition: color 0.2s, background-color 0.2s;
  user-select: none;
  flex-shrink: 0;
}
.emp-aside-footer:hover {
  color: var(--el-color-white);
  background-color: rgba(255, 255, 255, 0.08);
}
.preview-tip {
  padding: var(--app-space-3) var(--app-space-5);
  color: #7ee29a;
  font-size: var(--app-font-sm);
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  margin-top: var(--app-space-2);
  opacity: 0.9;
}

/* ===== 右侧主体:随侧边栏留出左边距 ===== */
.layout-body {
  position: relative;
  z-index: 1;
  height: 100%;
  overflow-y: auto;
  padding-left: var(--app-sidebar-width);
  transition: padding-left var(--app-duration-slow) var(--app-ease);
}

.layout-body.collapsed {
  padding-left: 0;
}

/* ===== header:sticky 白毛玻璃 ===== */
.emp-header {
  position: sticky;
  top: 0;
  z-index: 90;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  background-color: var(--app-header-glass-bg);
  -webkit-backdrop-filter: var(--app-glass-blur);
  backdrop-filter: var(--app-glass-blur);
  border-bottom: 1px solid var(--app-hairline);
}

.header-left {
  display: flex;
  align-items: center;
  gap: var(--app-space-4);
}

/* 圆形图标按钮:按下有回弹,悬停浅灰底 */
.sidebar-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  border: none;
  background: transparent;
  border-radius: var(--app-radius-full);
  color: var(--el-text-color-regular);
  cursor: pointer;
  transition: background-color var(--app-duration-fast) ease, color var(--app-duration-fast) ease,
    transform var(--app-duration-fast) ease-out;
}
.sidebar-toggle:hover {
  background-color: rgba(0, 0, 0, 0.06);
  color: var(--el-text-color-primary);
}
.sidebar-toggle:active {
  transform: scale(0.92);
}

.header-title {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.user-info {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: var(--app-space-2);
  padding: 6px 10px;
  border-radius: var(--app-radius-full);
  font-size: var(--app-font-md);
  transition: background-color var(--app-duration-fast) ease;
}
.user-info:hover {
  background-color: rgba(0, 0, 0, 0.05);
}

.emp-content {
  padding: 24px;
}

/* 收起侧边栏后页面内容水平居中 */
.emp-content.collapsed :deep(> *) {
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}

/* 窄屏收紧内容边距 */
@media (max-width: 640px) {
  .emp-content {
    padding: 16px;
  }

  .emp-header {
    padding: 0 16px;
  }
}
</style>
