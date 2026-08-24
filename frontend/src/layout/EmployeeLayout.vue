<template>
  <el-container class="emp-layout">
    <el-aside :width="collapsed ? '0px' : '220px'" class="emp-aside">
      <div class="logo">排班系统 · 员工端</div>
      <el-menu
        :default-active="$route.path"
        background-color="#001529"
        text-color="rgba(255,255,255,0.65)"
        active-text-color="#ffffff"
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
          <el-menu-item index="/employee/notifications">
            <el-icon><Bell /></el-icon>
            <span>通知消息</span>
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
    <el-container>
      <el-header class="emp-header">
        <div class="header-left">
          <el-button class="collapse-btn" text size="small" :title="collapsed ? '展开侧边栏' : '收起侧边栏'" @click="collapsed = !collapsed">
            <el-icon><Expand v-if="collapsed" /><Fold v-else /></el-icon>
            <span>{{ collapsed ? '展开' : '收起' }}</span>
          </el-button>
          <div class="header-title">{{ $route.meta.title }}</div>
        </div>
        <div style="display: flex; align-items: center; gap: 16px">
          <!-- 管理员预览员工选择器 -->
          <el-select
            v-if="authStore.role !== 'EMPLOYEE'"
            class="preview-select"
            :model-value="previewEmployeeNo"
            placeholder="选择预览员工"
            clearable
            filterable
            size="small"
            style="width: 180px"
            @update:model-value="onPreviewChange"
            @clear="onPreviewClear"
          >
            <el-option
              v-for="emp in employeeList"
              :key="emp.employeeNo"
              :label="`${emp.employeeNo} ${emp.name}`"
              :value="emp.employeeNo"
            />
          </el-select>
          <el-badge v-if="authStore.role === 'EMPLOYEE'" :value="unreadCount" :hidden="unreadCount === 0" :max="99" style="cursor: pointer" @click="$router.push('/employee/notifications')">
            <el-icon :size="20"><Bell /></el-icon>
          </el-badge>
          <el-dropdown @command="handleCommand">
            <span class="user-info">
              {{ authStore.user?.nickname || '未登录' }}
              <el-icon><ArrowDown /></el-icon>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>
      <el-main class="emp-content" :class="{ collapsed }">
        <router-view :key="$route.path" />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { ArrowDown, ArrowLeft, Bell, Calendar, Document, Expand, Fold, Switch } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { getUnreadCount } from '../api/notifications'
import { getEmployees } from '../api/employees'

const PREVIEW_KEY = 'shift_preview_employee_no'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const unreadCount = ref(0)
// 侧边栏收起状态：与员工端独立持久化，刷新后保持
const collapsed = ref(localStorage.getItem('emp-sidebar-collapsed') === '1')
watch(collapsed, v => localStorage.setItem('emp-sidebar-collapsed', v ? '1' : '0'))
const employeeList = ref([])
const previewEmployeeNo = ref(route.query.employeeNo || localStorage.getItem(PREVIEW_KEY) || '')
let refreshTimer = null

// 管理员加载员工列表用于选择
async function loadEmployeeList() {
  const role = authStore.role || localStorage.getItem('shift_role') || ''
  if (role === 'EMPLOYEE') return
  try {
    // 后端单页上限 100，逐页拉取全量员工供预览选择
    const pageSize = 100
    let page = 1
    let all = []
    let total = 0
    do {
      const res = await getEmployees({ page, pageSize, status: 1 })
      const items = res.items || []
      all = all.concat(items)
      total = res.total || 0
      page++
      // total>0 但本页无数据时终止，避免死循环
      if (items.length === 0) break
    } while (all.length < total)
    employeeList.value = all

    if (employeeList.value.length === 0) return

    // 校验当前预览员工是否存在于列表；无效（含空）回退第一个员工并同步 localStorage/query
    const exists = employeeList.value.some(e => e.employeeNo === previewEmployeeNo.value)
    if (!exists) {
      const first = employeeList.value[0]
      previewEmployeeNo.value = first.employeeNo
      localStorage.setItem(PREVIEW_KEY, first.employeeNo)
      router.replace({ path: route.path, query: { employeeNo: first.employeeNo } })
    }
  } catch (e) {
    console.error('加载员工列表失败', e)
  }
}

// 切换预览员工：保存到 localStorage 并跳转
function onPreviewChange(val) {
  previewEmployeeNo.value = val || ''
  if (val) localStorage.setItem(PREVIEW_KEY, val)
  else localStorage.removeItem(PREVIEW_KEY)
  const query = val ? { employeeNo: val } : {}
  router.push({ path: route.path, query })
}

function onPreviewClear() {
  previewEmployeeNo.value = ''
  localStorage.removeItem(PREVIEW_KEY)
  router.push({ path: route.path, query: {} })
}

// 菜单切换时保留预览员工参数
function handleMenuSelect(index) {
  const query = previewEmployeeNo.value ? { employeeNo: previewEmployeeNo.value } : {}
  router.push({ path: index, query })
}

// P3-15: 未读计数定时刷新
async function refreshUnreadCount() {
  try {
    const targetNo = previewEmployeeNo.value || route.query.employeeNo || localStorage.getItem(PREVIEW_KEY) || undefined
    const data = await getUnreadCount(targetNo || undefined)
    unreadCount.value = data?.count ?? 0
  } catch (e) {
    console.error('获取未读通知数失败', e)
  }
}

// 路由参数变化时同步
watch(() => route.query.employeeNo, (val) => {
  if (val) {
    previewEmployeeNo.value = val
    localStorage.setItem(PREVIEW_KEY, val)
  }
  refreshUnreadCount()
})

onMounted(async () => {
  const role = authStore.role || localStorage.getItem('shift_role') || ''
  // 管理员预览模式：仅允许查看班表，其他员工功能页重定向回班表
  if (role !== 'EMPLOYEE' && !['/employee/schedule'].includes(route.path)) {
    router.replace({ path: '/employee/schedule', query: route.query })
  }
  loadEmployeeList()
  if (authStore.isAuthenticated && !authStore.user) {
    try {
      // P3-26: 用户加载失败跳转登录
      await authStore.fetchCurrentUser()
      loadEmployeeList()
    } catch (e) {
      console.error('获取当前用户失败', e)
      // 仅会话失效（401）才登出并跳登录；网络错误保留会话
      if (e?.status === 401) {
        authStore.logout()
        router.push('/login')
      }
    }
  }
  refreshUnreadCount()
  refreshTimer = setInterval(refreshUnreadCount, 60000)
})

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
})

// 路由切换时刷新
watch(() => route.path, () => {
  refreshUnreadCount()
})

async function handleCommand(command) {
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
  background-color: #001529;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition: width 0.25s ease;
}
.logo {
  height: 60px;
  line-height: 60px;
  text-align: center;
  color: #fff;
  font-size: 15px;
  font-weight: 600;
}
.emp-aside :deep(.el-menu) {
  border-right: none;
  flex: 1;
}
.emp-aside-footer {
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
.emp-aside-footer:hover {
  color: #fff;
  background-color: rgba(255, 255, 255, 0.06);
}
.preview-tip {
  padding: 6px 12px;
  color: #67c23a;
  font-size: 12px;
  border-top: 1px solid rgba(255,255,255,0.1);
  margin-top: 4px;
  opacity: 0.85;
}
.emp-header {
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
.emp-content {
  overflow-y: auto;
}
/* 收起侧边栏后页面内容水平居中 */
.emp-content.collapsed :deep(> *) {
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}
.emp-content :deep(.el-table__body-wrapper) {
  max-height: calc(100vh - 240px);
  overflow-y: auto;
}
</style>