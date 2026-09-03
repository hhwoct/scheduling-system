<template>
  <div class="admin-layout">
    <router-view />
    <van-tabbar :model-value="activeTab" @change="onTabChange">
      <van-tabbar-item name="employees" icon="manager-o">员工管理</van-tabbar-item>
      <van-tabbar-item name="rules" icon="setting-o">规则配置</van-tabbar-item>
      <van-tabbar-item name="me" icon="user-o">我的</van-tabbar-item>
    </van-tabbar>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const TAB_PATHS = {
  employees: '/admin/employees',
  rules: '/admin/rules',
  me: '/admin/me'
}

const activeTab = computed(() => {
  const p = route.path
  if (p.startsWith('/admin/rules')) return 'rules'
  if (p.startsWith('/admin/me')) return 'me'
  return 'employees'
})

function onTabChange(name) {
  router.replace(TAB_PATHS[name] || '/admin/employees')
}
</script>

<style scoped>
.admin-layout {
  padding-bottom: calc(50px + constant(safe-area-inset-bottom));
  padding-bottom: calc(50px + env(safe-area-inset-bottom));
}
</style>
