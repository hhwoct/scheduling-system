<template>
  <div class="employee-layout">
    <router-view />
    <van-tabbar :model-value="activeTab" @change="onTabChange">
      <van-tabbar-item name="schedule" icon="calendar-o">班表</van-tabbar-item>
      <van-tabbar-item name="leaveSwap" icon="exchange">换班&请假</van-tabbar-item>
      <van-tabbar-item name="notifications" icon="bell" :badge="notifBadge">通知</van-tabbar-item>
      <van-tabbar-item name="profile" icon="user-o">我的</van-tabbar-item>
    </van-tabbar>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../../stores/notifications'

const route = useRoute()
const router = useRouter()
const notifStore = useNotificationStore()

const notifBadge = computed(() => {
  if (!notifStore.unreadCount) return undefined
  return notifStore.unreadCount > 99 ? '99+' : notifStore.unreadCount
})

// 手动精确控制高亮：不使用 Vant 的 route 模式（它按路由匹配链判断，
// /employee 会命中所有子页面，导致「班表」常驻高亮）
const TAB_PATHS = {
  schedule: '/employee',
  leaveSwap: '/employee/leave',
  notifications: '/employee/notifications',
  profile: '/employee/profile'
}

const activeTab = computed(() => {
  const p = route.path
  if (p.startsWith('/employee/leave') || p.startsWith('/employee/swap')) return 'leaveSwap'
  if (p.startsWith('/employee/notifications')) return 'notifications'
  if (p.startsWith('/employee/profile')) return 'profile'
  return 'schedule'
})

function onTabChange(name) {
  router.replace(TAB_PATHS[name] || '/employee')
}
</script>

<style scoped>
.employee-layout {
  padding-bottom: calc(50px + constant(safe-area-inset-bottom));
  padding-bottom: calc(50px + env(safe-area-inset-bottom));
}
</style>
