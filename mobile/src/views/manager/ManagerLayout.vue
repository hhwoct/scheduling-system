<template>
  <div class="manager-layout">
    <router-view />
    <van-tabbar :model-value="activeTab" @change="onTabChange">
      <van-tabbar-item name="schedule" icon="calendar-o">排班</van-tabbar-item>
      <van-tabbar-item name="mySchedule" icon="clock-o">我的班表</van-tabbar-item>
      <van-tabbar-item name="reviews" icon="todo-list-o" :badge="reviewBadge">审批</van-tabbar-item>
      <van-tabbar-item name="employees" icon="manager-o">员工管理</van-tabbar-item>
      <van-tabbar-item name="notifications" icon="bell" :badge="notifBadge">通知</van-tabbar-item>
      <van-tabbar-item name="me" icon="user-o">我的</van-tabbar-item>
    </van-tabbar>
  </div>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useNotificationStore } from '../../stores/notifications'
import { useReviewStore } from '../../stores/reviews'

const route = useRoute()
const router = useRouter()
const notifStore = useNotificationStore()
const reviewStore = useReviewStore()

const notifBadge = computed(() => {
  if (!notifStore.unreadCount) return undefined
  return notifStore.unreadCount > 99 ? '99+' : notifStore.unreadCount
})

const reviewBadge = computed(() => {
  if (!reviewStore.pendingTotal) return undefined
  return reviewStore.pendingTotal > 99 ? '99+' : reviewStore.pendingTotal
})

// 手动精确控制高亮（同员工端：Vant route 模式会按路由匹配链误判）
const TAB_PATHS = {
  schedule: '/manager',
  mySchedule: '/manager/my-schedule',
  reviews: '/manager/reviews',
  employees: '/manager/employees',
  notifications: '/manager/notifications',
  me: '/manager/me'
}

const activeTab = computed(() => {
  const p = route.path
  if (p.startsWith('/manager/my-schedule')) return 'mySchedule'
  if (p.startsWith('/manager/reviews')) return 'reviews'
  if (p.startsWith('/manager/employees')) return 'employees'
  if (p.startsWith('/manager/notifications')) return 'notifications'
  // 「我的」下的请假与换班/修改密码 页面高亮「我的」Tab
  if (p.startsWith('/manager/me') || p.startsWith('/manager/my-leave-swap')) return 'me'
  return 'schedule'
})

function onTabChange(name) {
  router.replace(TAB_PATHS[name] || '/manager')
}

onMounted(() => {
  reviewStore.fetchCounts()
})
</script>

<style scoped>
.manager-layout {
  padding-bottom: calc(50px + constant(safe-area-inset-bottom));
  padding-bottom: calc(50px + env(safe-area-inset-bottom));
}
</style>
