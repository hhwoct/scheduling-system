<template>
  <!-- 页面骨架规范模板页:
       .page = 单列纵向流,hero 在顶 = 本页核心内容 -->
  <div class="page">
    <section class="page-hero">
      <div class="page-hero__title">{{ store?.name || '--' }}</div>
      <div class="page-hero__sub">门店编码 {{ store?.code || '--' }}{{ store?.address ? ' · ' + store.address : '' }}</div>
      <div class="hero-stats">
        <div class="hero-stat">
          <div class="hero-stat__value">{{ stats.employeeCount ?? '--' }}</div>
          <div class="hero-stat__label">员工总数</div>
          <div class="hero-stat__sub">{{ stats.fullTimeCount != null && stats.partTimeCount != null ? stats.fullTimeCount + ' 全职 · ' + stats.partTimeCount + ' 兼职' : '' }}</div>
        </div>
        <div class="hero-stat">
          <div class="hero-stat__value">{{ stats.shiftCount ?? '--' }}</div>
          <div class="hero-stat__label">班次数量</div>
        </div>
        <div class="hero-stat">
          <div class="hero-stat__value">{{ stats.workstationCount ?? '--' }}</div>
          <div class="hero-stat__label">工作站</div>
        </div>
      </div>
    </section>

    <el-card>
      <template #header>门店信息</template>
      <el-descriptions :column="1" border>
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
/* 模板页不再有私有布局样式:单列骨架、hero、卡片间距全部来自 page-layout.css */
</style>
