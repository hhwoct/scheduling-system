<template>
  <el-container class="main-layout">
    <el-aside width="220px" class="main-aside">
      <div class="logo">排班系统</div>
      <el-menu
        :default-active="$route.path"
        router
        background-color="#001529"
        text-color="rgba(255,255,255,0.65)"
        active-text-color="#ffffff"
      >
        <el-menu-item index="/dashboard">
          <el-icon><DataBoard /></el-icon>
          <span>首页概览</span>
        </el-menu-item>
        <el-sub-menu index="basic">
          <template #title><span>基础数据</span></template>
          <el-menu-item index="/employees">员工管理</el-menu-item>
          <el-menu-item index="/workstations">工作站管理</el-menu-item>
          <el-menu-item index="/shift-templates">班次管理</el-menu-item>
          <el-menu-item index="/rules">规则配置</el-menu-item>
          <el-menu-item index="/peak-hours">高峰时段</el-menu-item>
          <el-menu-item index="/staffing-requirements">人数需求</el-menu-item>
          <el-menu-item index="/skill-matrix">技能等级</el-menu-item>
        </el-sub-menu>
        <el-sub-menu index="schedule">
          <template #title><span>排班管理</span></template>
          <el-menu-item index="/schedules/generate">一键排班</el-menu-item>
          <el-menu-item index="/schedules/view">排班查看</el-menu-item>
          <el-menu-item index="/reports">排班报表</el-menu-item>
          <el-menu-item index="/audit-logs">审计日志</el-menu-item>
          <el-menu-item index="/leave-review">请假审批</el-menu-item>
          <el-menu-item index="/swap-review">换班审批</el-menu-item>
          <el-menu-item index="/notifications">通知消息</el-menu-item>
        </el-sub-menu>
        <el-menu-item index="/employee/schedule" @click="goEmployeePreview">
          <el-icon><Calendar /></el-icon>
          <span>员工端（我的班表）</span>
        </el-menu-item>
      </el-menu>
    </el-aside>
    <el-container>
      <el-header class="main-header">
        <div class="header-title">{{ $route.meta.title }}</div>
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
                <el-dropdown-item command="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>
      <el-main class="main-content">
        <!-- 只对页面内容加 key，避免整个布局（含 sidebar）重新挂载 -->
        <router-view :key="$route.fullPath" />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowDown, Bell, Calendar, DataBoard } from '@element-plus/icons-vue'
import { ElMessageBox } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { getUnreadCount } from '../api/notifications'

const router = useRouter()
const authStore = useAuthStore()
const unreadCount = ref(0)

onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    authStore.fetchCurrentUser().catch(() => {})
  }
  try {
    const data = await getUnreadCount()
    unreadCount.value = data?.count ?? 0
  } catch {}
})

// 管理员进入员工端时默认选择第一个员工（E001）预览
function goEmployeePreview() {
  router.push({ path: '/employee/schedule', query: { employeeNo: 'E001' } })
}

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
.main-layout {
  height: 100%;
}
.main-aside {
  background-color: #001529;
}
.logo {
  height: 60px;
  line-height: 60px;
  text-align: center;
  color: #fff;
  font-size: 18px;
  font-weight: 600;
}
.main-aside :deep(.el-menu) {
  border-right: none;
}
.main-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #fff;
  border-bottom: 1px solid #e4e7ed;
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
</style>