<template>
  <div>
    <el-row :gutter="16">
      <el-col :span="8">
        <el-card shadow="hover">
          <div class="stat-value">{{ stats.employeeCount ?? '--' }}</div>
          <div class="stat-label">员工数量</div>
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

    <el-card style="margin-top: 16px">
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
import { useAuthStore } from '../stores/auth'

const authStore = useAuthStore()
const store = ref(null)
const stats = reactive({})

onMounted(async () => {
  try {
      const [storeRes, statsRes] = await Promise.all([
        getCurrentStore(),
        fetch('/api/dashboard/stats', { headers: { Authorization: `Bearer ${localStorage.getItem('shift_token')}` } }).then(r => r.json())
      ])
      store.value = storeRes
      if (statsRes?.data) {
        stats.employeeCount = statsRes.data.employeeCount
        stats.shiftCount = statsRes.data.shiftCount
        stats.workstationCount = statsRes.data.workstationCount
      }
  } catch {
    store.value = null
  }
  if (authStore.isAuthenticated && !authStore.user) {
    authStore.fetchCurrentUser().catch(() => {})
  }
})
</script>

<style scoped>
.stat-value {
  font-size: 32px;
  font-weight: 700;
  color: #409eff;
}
.stat-label {
  margin-top: 8px;
  color: #909399;
}
</style>
