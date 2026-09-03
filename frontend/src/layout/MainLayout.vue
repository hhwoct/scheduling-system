<template>
  <div class="main-layout">
    <!-- 固定深蓝毛玻璃侧边栏:内容滚动时透过模糊看到极淡的品牌 aurora 底 -->
    <aside class="main-aside" :class="{ collapsed }" :style="{ width: collapsed ? '0px' : 'var(--app-sidebar-width)' }">
      <div class="logo">排班系统</div>
      <el-menu
        :default-active="$route.path"
        @select="handleMenuSelect"
      >
        <!-- 所有门店统一:admin 扁平菜单 / 店长分组菜单 -->
        <template v-if="authStore.role !== 'EMPLOYEE'">
          <!-- admin：页面不多，不再分组 -->
          <template v-if="!isStoreManager">
            <el-menu-item index="/dashboard">
              <el-icon><DataBoard /></el-icon>
              <span>首页概览</span>
            </el-menu-item>
            <el-menu-item index="/employees">
              <el-icon><User /></el-icon>
              <span>员工管理</span>
            </el-menu-item>
            <el-menu-item index="/rules">
              <el-icon><Setting /></el-icon>
              <span>规则配置</span>
            </el-menu-item>
            <el-menu-item v-if="authStore.username === SUPER_ADMIN_USERNAME" index="/date-parameters">
              <el-icon><Calendar /></el-icon>
              <span>日期参数</span>
            </el-menu-item>
            <el-menu-item index="/schedules/view">
              <el-icon><View /></el-icon>
              <span>排班查看</span>
            </el-menu-item>
            <el-menu-item v-if="authStore.username === SUPER_ADMIN_USERNAME" index="/audit-logs">
              <el-icon><Document /></el-icon>
              <span>审计日志</span>
            </el-menu-item>
          </template>
          <!-- 店长：功能多，保留分组 -->
          <template v-else>
            <el-menu-item index="/dashboard">
              <el-icon><DataBoard /></el-icon>
              <span>首页概览</span>
            </el-menu-item>
            <el-sub-menu index="basic">
              <template #title><span>基础数据</span></template>
              <el-menu-item index="/employees">员工管理</el-menu-item>
              <el-menu-item index="/rules">规则配置</el-menu-item>
            </el-sub-menu>
            <!-- 门店级运营配置：仅店长可见 -->
            <el-sub-menu index="store-ops">
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
              <el-menu-item v-if="isStoreManager" index="/leave-review">请假审批</el-menu-item>
              <el-menu-item v-if="isStoreManager" index="/swap-review">换班审批</el-menu-item>
            </el-sub-menu>
          </template>
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
    </aside>
    <div class="layout-body" :class="{ collapsed }">
      <!-- sticky 毛玻璃 header:内容上滑时透过模糊 -->
      <header class="main-header">
        <div class="header-left">
          <!-- 侧边栏收起/展开:header 左上角圆形图标按钮(macOS 邮件/备忘录同款) -->
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
      </header>
      <main class="main-content" :class="{ collapsed }">
        <!-- 只对页面内容加 key，避免整个布局（含 sidebar）每次路由都重新挂载 -->
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
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowDown, Calendar, DataBoard, Document, Expand, Fold, Setting, User, View } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { SUPER_ADMIN_USERNAME, STORE_MANAGER_USERNAME } from '../constants/config'
import ChangePasswordDialog from '../components/ChangePasswordDialog.vue'
import NotificationsPanel from '../components/NotificationsPanel.vue'

const router = useRouter()
const authStore = useAuthStore()

// 店长判定:按「角色或店长用户名」双条件
// (E001/A001 的数据库角色可能是 SYSTEM_ADMIN,按角色判定会失效)
const isStoreManager = computed(() => authStore.role === 'STORE_MANAGER' || authStore.username === STORE_MANAGER_USERNAME || authStore.username === 'A001')

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
    // 预览员工端:各门店店长默认看自己门店第一个全职员工
    router.push({ path: index, query: { employeeNo: authStore.storeId === '2' ? 'A002' : 'E001' } })
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
  position: relative;
  height: 100%;
}

/* 极淡的品牌 aurora 底:给毛玻璃侧边栏提供可被模糊的层次,内容区几乎无感 */
.main-layout::before {
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
.main-aside {
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

.main-aside.collapsed {
  border-right: none;
  box-shadow: none;
}

.logo {
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #ffffff;
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.04em;
}

/* ===== 菜单:胶囊高亮 ===== */
.main-aside :deep(.el-menu) {
  --el-menu-bg-color: transparent;
  --el-menu-text-color: var(--app-sidebar-text);
  --el-menu-active-color: var(--app-sidebar-text-active);
  --el-menu-hover-bg-color: transparent;
  --el-menu-item-hover-fill: transparent;
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  border-right: none;
  padding: 8px;
}

.main-aside :deep(.el-menu-item),
.main-aside :deep(.el-sub-menu__title) {
  height: 40px;
  line-height: 40px;
  border-radius: var(--app-radius-md);
  margin: 2px 0;
  padding: 0 12px !important;
  transition: background-color var(--app-duration-fast) ease, color var(--app-duration-fast) ease;
}

.main-aside :deep(.el-menu-item:hover),
.main-aside :deep(.el-sub-menu__title:hover) {
  background-color: var(--app-sidebar-hover-bg);
}

.main-aside :deep(.el-menu-item.is-active),
.main-aside :deep(.el-menu-item.is-active:hover) {
  background-color: var(--app-sidebar-active-bg);
  color: var(--app-sidebar-text-active);
  font-weight: 600;
}

/* 子菜单项缩进(菜单已自带 40px,盖掉统一 12px 即可) */
.main-aside :deep(.el-sub-menu .el-menu-item) {
  padding-left: 28px !important;
}

.aside-footer {
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
.aside-footer:hover {
  color: var(--el-color-white);
  background-color: rgba(255, 255, 255, 0.08);
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

/* ===== header:sticky 白毛玻璃,内容上滑透出模糊 ===== */
.main-header {
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

.main-content {
  padding: 24px;
}

/* 收起侧边栏后页面内容水平居中（1400px 内居中，超出贴边） */
.main-content.collapsed :deep(> *) {
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}

/* 窄屏收紧内容边距 */
@media (max-width: 640px) {
  .main-content {
    padding: 16px;
  }

  .main-header {
    padding: 0 16px;
  }
}
</style>
