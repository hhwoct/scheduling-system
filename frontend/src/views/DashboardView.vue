<template>
  <div>
    <el-row :gutter="16">
      <el-col :span="8">
        <el-card shadow="hover">
          <div class="stat-value">{{ stats.employeeCount ?? '--' }}</div>
          <div class="stat-label">员工数量</div>
          <div class="stat-sub-label">{{ stats.fullTimeCount != null && stats.partTimeCount != null ? `${stats.fullTimeCount}全职 + ${stats.partTimeCount}兼职` : '' }}</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="hover">
          <div class="stat-value">{{ stats.shiftCount ?? '--' }}</div>
          <div class="stat-label">班次数量</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="hover">
          <div class="stat-value">{{ stats.workstationCount ?? '--' }}</div>
          <div class="stat-label">工作站数量</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card style="margin-top: var(--app-space-6)">
      <template #header>当前门店</template>
      <el-descriptions :column="2" border>
        <el-descriptions-item label="门店编码">{{ store?.code || '--' }}</el-descriptions-item>
        <el-descriptions-item label="门店名称">{{ store?.name || '--' }}</el-descriptions-item>
        <el-descriptions-item label="地址">{{ store?.address || '--' }}</el-descriptions-item>
        <el-descriptions-item label="最大员工数">{{ store?.maxEmployeeCount ?? '--' }}</el-descriptions-item>
      </el-descriptions>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { getCurrentStore } from '../api/store'
import request from '../utils/request'
import { useAuthStore } from '../stores/auth'
import { API_ROUTES } from '../constants/api'

const authStore = useAuthStore()
const store = ref(null)
const stats = reactive({})

onMounted(async () => {
  const [storeRes, statsRes] = await Promise.allSettled([
    getCurrentStore(),
    request.get(API_ROUTES.DASHBOARD.STATS)
  ])

  // 分别处理结果：一个请求失败不丢弃另一个有效数据
  if (storeRes.status === 'fulfilled') {
    store.value = storeRes.value
  } else {
    console.error('加载门店信息失败', storeRes.reason)
  }

  if (statsRes.status === 'fulfilled' && statsRes.value?.data) {
    stats.employeeCount = statsRes.value.data.employeeCount
    stats.fullTimeCount = statsRes.value.data.fullTimeCount
    stats.partTimeCount = statsRes.value.data.partTimeCount
    stats.shiftCount = statsRes.value.data.shiftCount
    stats.workstationCount = statsRes.value.data.workstationCount
  } else {
    console.error('加载统计数据失败', statsRes.reason)
    // 显示部分数据，不全部丢弃
  }

  if (authStore.isAuthenticated && !authStore.user) {
    try {
      await authStore.fetchCurrentUser()
    } catch (e) {
      console.error('获取当前用户失败', e)
    }
  }
})
</script>

<style scoped>
.stat-value {
  font-size: var(--app-font-display);
  font-weight: 700;
  color: var(--el-color-primary);
}
.stat-label {
  margin-top: var(--app-space-4);
  color: var(--el-text-color-secondary);
}
.stat-sub-label {
  margin-top: var(--app-space-2);
  font-size: var(--app-font-base);
  color: var(--el-color-success);
}
</style>
