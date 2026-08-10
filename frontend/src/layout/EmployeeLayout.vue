<template>
  <el-container class="emp-layout">
    <el-aside width="200px" class="emp-aside">
      <div class="logo">排班系统 · 员工端</div>
      <el-menu
        :default-active="$route.path"
        router
        background-color="#001529"
        text-color="rgba(255,255,255,0.65)"
        active-text-color="#ffffff"
      >
        <el-menu-item index="/employee/schedule">
          <el-icon><Calendar /></el-icon>
          <span>我的班表</span>
        </el-menu-item>
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
      </el-menu>
    </el-aside>
    <el-container>
      <el-header class="emp-header">
        <div class="header-title">{{ $route.meta.title }}</div>
        <div style="display: flex; align-items: center; gap: 16px">
          <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99" style="cursor: pointer" @click="$router.push('/employee/notifications')">
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
      <el-main class="emp-content">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ArrowDown, Bell, Calendar, Document, Switch } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'
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

function handleCommand(command) {
  if (command === 'logout') {
    authStore.logout()
    router.push('/login')
  }
}
</script>

<style scoped>
.emp-layout {
  height: 100%;
}
.emp-aside {
  background-color: #001529;
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
}
.emp-header {
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
.emp-content {
  overflow-y: auto;
}
</style>