<template>
  <!-- 页面骨架规范模板页(权限分流版):
       .page = 单列纵向流;.page-hero = 本页核心内容
       超管看旗下门店总览,门店账号看自己门店 -->
  <div class="page">
    <!-- ===== 超管:旗下门店总览 ===== -->
    <template v-if="isSystemAdmin">
      <section class="page-hero">
        <div class="page-hero__title">旗下门店</div>
        <div class="page-hero__sub">全部门店的运营概况{{ stores.length ? ' · 数据来自各门店实时统计' : '' }}</div>
        <div class="hero-stats">
          <div class="hero-stat">
            <div class="hero-stat__value">{{ stores.length || '--' }}</div>
            <div class="hero-stat__label">门店数</div>
          </div>
          <div class="hero-stat">
            <div class="hero-stat__value">{{ totalFullTime ?? '--' }}</div>
            <div class="hero-stat__label">员工</div>
          </div>
          <div class="hero-stat">
            <div class="hero-stat__value">{{ totalShifts ?? '--' }}</div>
            <div class="hero-stat__label">班次总数</div>
          </div>
        </div>
      </section>

      <el-card v-loading="storesLoading">
        <template #header>门店列表</template>
        <div v-if="storesFailed" class="u-text-hint u-mb-4">
          门店总览数据暂不可用（后端 /stores 接口未部署时显示此提示）。
        </div>
        <el-empty v-if="!storesLoading && storesFailed && stores.length === 0" description="暂无门店数据" />
        <div v-else class="store-list">
          <div v-for="s in stores" :key="s.id" class="store-card">
            <div class="store-card__main">
              <div class="store-card__name">{{ s.name }}</div>
              <div class="store-card__sub">{{ s.code }}{{ s.address ? ' · ' + s.address : '' }}</div>
            </div>
            <div class="store-card__metrics">
              <div class="metric">
                <div class="metric__value">{{ s.fullTimeCount ?? '--' }}</div>
                <div class="metric__label">员工</div>
              </div>
              <div class="metric">
                <div class="metric__value">{{ s.shiftCount ?? '--' }}</div>
                <div class="metric__label">班次</div>
              </div>
              <div class="metric">
                <div class="metric__value">{{ s.workstationCount ?? '--' }}</div>
                <div class="metric__label">工作站</div>
              </div>
            </div>
          </div>
        </div>
      </el-card>
    </template>

    <!-- ===== 门店账号:自己门店 ===== -->
    <template v-else>
      <section class="page-hero">
        <div class="page-hero__title">{{ store?.name || '--' }}</div>
        <div class="page-hero__sub">门店编码 {{ store?.code || '--' }}{{ store?.address ? ' · ' + store.address : '' }}</div>
        <div class="hero-stats">
          <div class="hero-stat">
            <div class="hero-stat__value">{{ stats.fullTimeCount ?? '--' }}</div>
            <div class="hero-stat__label">员工</div>
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
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { getCurrentStore, getStores } from '../api/store'
import request from '../utils/request'
import { useAuthStore } from '../stores/auth'
import { API_ROUTES } from '../constants/api'
import { SUPER_ADMIN_USERNAME } from '../constants/config'

const authStore = useAuthStore()
const store = ref(null)
const stats = reactive({})

// 超管(admin 用户名 或 SYSTEM_ADMIN 角色)看全部门店
const isSystemAdmin = computed(() =>
  authStore.role === 'SYSTEM_ADMIN' || authStore.username === SUPER_ADMIN_USERNAME
)

// ===== 超管门店总览 =====
const stores = ref([])
const storesLoading = ref(false)
const storesFailed = ref(false)

const totalFullTime = computed(() => {
  if (!stores.value.length) return null
  return stores.value.reduce((sum, s) => sum + (s.fullTimeCount || 0), 0)
})
const totalShifts = computed(() => {
  if (!stores.value.length) return null
  return stores.value.reduce((sum, s) => sum + (s.shiftCount || 0), 0)
})

async function loadStores() {
  storesLoading.value = true
  storesFailed.value = false
  try {
    stores.value = await getStores()
  } catch (e) {
    storesFailed.value = true
    stores.value = []
  } finally {
    storesLoading.value = false
  }
}

onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    try {
      await authStore.fetchCurrentUser()
    } catch (e) {
      console.error('获取当前用户失败', e)
    }
  }

  if (isSystemAdmin.value) {
    await loadStores()
    return
  }

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
})
</script>

<style scoped>
/* 门店列表:单列卡片,每张卡左侧门店名、右侧指标 */
.store-list {
  display: flex;
  flex-direction: column;
  gap: var(--app-space-4);
}

.store-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--app-space-4);
  padding: var(--app-space-5);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--app-radius-md);
}

.store-card__name {
  font-size: var(--app-font-lg);
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.store-card__sub {
  margin-top: var(--app-space-2);
  font-size: var(--app-font-sm);
  color: var(--el-text-color-secondary);
}

.store-card__metrics {
  display: flex;
  gap: var(--app-space-6);
}

.metric {
  text-align: center;
  min-width: 64px;
}

.metric__value {
  font-size: var(--app-font-xl);
  font-weight: 700;
  color: var(--app-brand);
  line-height: 1.2;
}

.metric__label {
  margin-top: var(--app-space-1);
  font-size: var(--app-font-xs);
  color: var(--el-text-color-secondary);
}
</style>
