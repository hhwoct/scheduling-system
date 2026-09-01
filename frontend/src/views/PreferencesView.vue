<template>
  <div class="pref-page">
    <div class="page-header">
      <h2>偏好学习</h2>
      <el-button type="primary" :loading="rebuilding" @click="handleRebuild">立即重建学习</el-button>
    </div>

    <!-- 统计卡 -->
    <el-row :gutter="16" class="stat-row">
      <el-col :span="6"><div class="stat-card"><div class="stat-num">{{ stats.learnedPeriods }}</div><div class="stat-label">已学习周期</div></div></el-col>
      <el-col :span="6"><div class="stat-card"><div class="stat-num">{{ stats.sampleDays }}</div><div class="stat-label">学习样本（人·日）</div></div></el-col>
      <el-col :span="6"><div class="stat-card"><div class="stat-num">{{ stats.coveragePct }}%</div><div class="stat-label">样本覆盖率</div></div></el-col>
      <el-col :span="6"><div class="stat-card" :class="{ 'trend-up': stats.adherencePct > 0 }"><div class="stat-num">{{ stats.adherencePct }}%</div><div class="stat-label">最新贴合率</div></div></el-col>
    </el-row>
    <div class="weight-tip">偏好权重：<b>{{ stats.weight }}</b>（规则 preference_learning_weight，取值范围 0~1：0 = 只看技能，1 = 完全按店长偏好，中间值按比例混合。排序分 = 技能分 × (1 − 权重) + 偏好分 × 权重）</div>

    <!-- 未开启引导：权重为 0 时展示 -->
    <el-alert
      v-if="stats.weight <= 0"
      type="info"
      :closable="false"
      show-icon
      title="偏好学习未开启"
      description="当前排班生成不使用偏好学习（与原有行为完全一致）。如需开启：在「规则配置」中将 preference_learning_weight 设为 0~1 之间的小数（建议从 0.3 试起，1 = 完全按店长偏好），并在发布排班后点击「立即重建学习」积累店长偏好。技能门槛、工时上限等硬约束始终不受偏好影响。"
      class="section"
    />

    <!-- 趋势 -->
    <el-card class="section" v-if="trends.length">
      <template #header>贴合率趋势（每期已发布排班）</template>
      <el-table :data="trends" size="small">
        <el-table-column prop="planName" label="排班计划" min-width="220" />
        <el-table-column label="发布时间" width="160">
          <template #default="{ row }">{{ formatTime(row.publishedAt) }}</template>
        </el-table-column>
        <el-table-column prop="adherencePct" label="贴合率" width="100"><template #default="{ row }">{{ row.adherencePct }}%</template></el-table-column>
        <el-table-column prop="coveragePct" label="覆盖率" width="100"><template #default="{ row }">{{ row.coveragePct }}%</template></el-table-column>
        <el-table-column prop="sampleDays" label="学习样本" width="100" />
        <el-table-column prop="adjustments" label="店长调整数" width="100"><template #default="{ row }">{{ row.adjustments ?? '-' }}</template></el-table-column>
      </el-table>
      <div class="weight-tip">店长调整数 = 发布时与生成时「员工×日期」安排不一致的条数；随学习生效应逐步下降。</div>
    </el-card>

    <!-- Top 偏好 -->
    <el-card class="section">
      <template #header>Top 偏好（店长认可最多的安排）</template>
      <el-table :data="top" size="small" max-height="360">
        <el-table-column prop="employeeNo" label="工号" width="80" />
        <el-table-column prop="employeeName" label="姓名" width="90" />
        <el-table-column label="日期类型" width="90">
          <template #default="{ row }">{{ dayTypeLabel(row.dayType) }}</template>
        </el-table-column>
        <el-table-column label="偏好安排" min-width="160">
          <template #default="{ row }">{{ row.shiftCode ? row.shiftCode + ' 班次' : (row.freq >= 0 ? '工作站/休息' : '') }}</template>
        </el-table-column>
        <el-table-column prop="freq" label="认可次数" width="90" />
      </el-table>
    </el-card>

    <!-- 矩阵 -->
    <el-card class="section">
      <template #header>
        <div class="u-row-between">
          <span>工作站偏好矩阵（freq ≥ 3 的样本）</span>
          <el-select v-model="matrixDayType" style="width: 140px" @change="loadMatrix">
            <el-option label="全部类型" value="" />
            <el-option label="平日" value="WORKDAY" />
            <el-option label="周末" value="WEEKEND" />
            <el-option label="节假日" value="HOLIDAY" />
          </el-select>
        </div>
      </template>
      <el-table :data="matrix" size="small" max-height="420">
        <el-table-column prop="employeeNo" label="工号" width="80" />
        <el-table-column prop="employeeName" label="姓名" width="90" />
        <el-table-column prop="workstationCode" label="工作站" min-width="110" />
        <el-table-column label="日期类型" width="90">
          <template #default="{ row }">{{ dayTypeLabel(row.dayType) }}</template>
        </el-table-column>
        <el-table-column label="认可频次" width="140">
          <template #default="{ row }">
            <el-progress :percentage="Math.min(row.freq, 100)" :stroke-width="12" :format="() => row.freq + ' 次'" />
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getPreferenceMatrix, getPreferenceStats, getPreferenceTop, getPreferenceTrends, rebuildPreferences } from '../api/preferences'

const stats = ref({ learnedPeriods: 0, sampleDays: 0, coveragePct: 0, adherencePct: 0, weight: 0 })
const trends = ref([])
const top = ref([])
const matrix = ref([])
const matrixDayType = ref('')
const rebuilding = ref(false)

const DAY_TYPE_LABELS = { WORKDAY: '平日', WEEKEND: '周末', HOLIDAY: '节假日' }
const dayTypeLabel = (t) => DAY_TYPE_LABELS[t] || t

function formatTime(t) {
  if (!t) return ''
  return String(t).replace('T', ' ').slice(0, 16)
}

async function loadStats() {
  try { stats.value = await getPreferenceStats() } catch (e) { console.error(e) }
}
async function loadTrends() {
  try { trends.value = await getPreferenceTrends() } catch (e) { console.error(e) }
}
async function loadTop() {
  try { top.value = await getPreferenceTop(20) } catch (e) { console.error(e) }
}
async function loadMatrix() {
  try { matrix.value = await getPreferenceMatrix(matrixDayType.value || undefined) } catch (e) { console.error(e) }
}

async function handleRebuild() {
  rebuilding.value = true
  try {
    stats.value = await rebuildPreferences()
    ElMessage.success('偏好学习已重建')
    await Promise.all([loadTrends(), loadTop(), loadMatrix()])
  } catch (e) {
    ElMessage.error('重建失败：' + (e.message || '网络错误'))
  } finally {
    rebuilding.value = false
  }
}

onMounted(() => {
  loadStats()
  loadTrends()
  loadTop()
  loadMatrix()
})
</script>

<style scoped>
.pref-page { padding: var(--app-space-2); }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--app-space-5); }
.stat-row { margin-bottom: var(--app-space-4); }
.stat-card {
  background: var(--el-fill-color-light); border-radius: var(--app-radius-md); padding: 14px; text-align: center;
  border: 1px solid var(--el-border-color-lighter);
}
.stat-num { font-size: var(--app-font-display); font-weight: 700; color: var(--el-color-primary); }
.stat-card.trend-up .stat-num { color: var(--el-color-success); }
.stat-label { font-size: var(--app-font-sm); color: var(--el-text-color-secondary); margin-top: var(--app-space-2); }
.weight-tip { font-size: var(--app-font-sm); color: var(--el-text-color-secondary); margin: var(--app-space-3) 0 var(--app-space-5); }
.section { margin-bottom: var(--app-space-6); }
</style>
