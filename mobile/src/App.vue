<template>
  <router-view />
</template>

<script setup>
import { watch, onMounted, onUnmounted } from 'vue'
import { useAuthStore } from './stores/auth'
import { useNotificationStore } from './stores/notifications'

const auth = useAuthStore()
const notif = useNotificationStore()

// 登录后开始前台轮询未读数；登出即停
watch(
  () => auth.token,
  (t) => {
    if (t) notif.startPolling()
    else notif.stopPolling()
  },
  { immediate: true }
)

// 页签从后台恢复可见时立即补拉一次（后台定时器会被浏览器节流）
function onVisibility() {
  if (document.visibilityState === 'visible' && auth.isAuthenticated) {
    notif.fetchUnread()
  }
}

onMounted(() => document.addEventListener('visibilitychange', onVisibility))
onUnmounted(() => document.removeEventListener('visibilitychange', onVisibility))
</script>
