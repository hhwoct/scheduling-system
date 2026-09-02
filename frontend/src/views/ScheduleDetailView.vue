<template>
  <div>
    <!-- 顶部工具条：计划选择 + 生成重排 -->
    <el-card class="u-mb-5">
      <div class="u-row-between u-wrap u-gap-4">
        <div class="u-row u-wrap u-gap-4 u-flex-1">
          <el-select v-model="planId" placeholder="选择排班计划" style="width: 340px" @change="onPlanChange">
            <el-option v-for="p in plans" :key="p.id" :label="p.planName + '（' + p.startDate + ' ~ ' + p.endDate + '）'" :value="p.id">
              <span>{{ p.planName }}</span>
              <el-tag class="u-ml-2" size="small" :type="p.source === 'REAL' ? 'success' : 'primary'">
                {{ p.source === 'REAL' ? '真实班表' : '算法排班' }}
              </el-tag>
              <el-tag class="u-ml-1" size="small" :type="p.status === 'PUBLISHED' ? 'success' : 'info'">
                {{ p.status === 'PUBLISHED' ? '已发布' : '草稿' }}
              </el-tag>
            </el-option>
          </el-select>
          <el-button type="primary" :loading="generating" @click="openGenerate">
            <el-icon class="u-mr-1"><MagicStick /></el-icon>生成 / 重排
          </el-button>
          <el-button :disabled="!planId" @click="reload">刷新</el-button>
        </div>
        <div class="u-row u-gap-4">
          <el-radio-group v-model="compareOn">
            <el-radio-button :value="false">单看当前计划</el-radio-button>
            <el-radio-button :value="true">真实 vs 算法对比</el-radio-button>
          </el-radio-group>
          <el-radio-group v-model="viewMode">
            <el-radio-button value="week">周视图</el-radio-button>
            <el-radio-button value="month">整月对比</el-radio-button>
            <el-radio-button value="day">日明细</el-radio-button>
          </el-radio-group>
        </div>
      </div>
    </el-card>

    <!-- 吻合率概览 -->
    <div v-if="compareOn && compare" class="u-row u-wrap u-gap-4 u-mb-5">
      <el-card shadow="hover" class="stat-card">
        <div class="stat-num">{{ compare.overall.workRestMatchRate }}%</div>
        <div class="stat-label">上班/休息吻合率（{{ compare.overall.matchedCells }}/{{ compare.overall.cells }} 人·天）</div>
      </el-card>
      <el-card shadow="hover" class="stat-card">
        <div class="stat-num">{{ compare.overall.shiftMatchRate ?? '--' }}</div>
        <div class="stat-label">班次吻合率（标注了班次的岗位 {{ compare.overall.shiftMatched }}/{{ compare.overall.shiftComparable }}）</div>
      </el-card>
      <el-card shadow="hover" class="stat-card">
        <div class="stat-num">{{ compare.overall.realWorkCells }} / {{ compare.overall.algoWorkCells }}</div>
        <div class="stat-label">真实 / 算法 上班人·天</div>
      </el-card>
      <el-card shadow="hover" class="stat-card">
        <div class="stat-num">{{ compare.range.days }}</div>
        <div class="stat-label">对比天数（{{ compare.range.startDate }} ~ {{ compare.range.endDate }}）</div>
      </el-card>
    </div>

    <!-- 周视图 -->
    <el-card v-if="viewMode === 'week' && !loading" class="u-mb-5">
      <template #header>
        <div class="u-row-between">
          <span>周视图（员工 × 日期）</span>
          <el-date-picker v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px" @change="loadWeek" />
        </div>
      </template>
      <el-table :data="weekRows" border size="small">
        <el-table-column prop="employeeName" label="员工" min-width="110" show-overflow-tooltip fixed>
          <template #default="{ row }">
            <div>{{ row.employeeNo }}</div>
            <div class="u-text-sm u-text-hint">{{ row.employeeName }} · {{ row.department }}</div>
          </template>
        </el-table-column>
        <el-table-column v-for="d in weekDates" :key="d" :label="shortLabel(d)" min-width="86" show-overflow-tooltip align="center">
          <template #default="{ row }">
            <div class="cell-wrap" :class="weekCell(row, d).baseCls">
              <span v-if="weekCell(row, d).rest" class="base-rest-text">休</span>
              <span v-else class="base-code">{{ weekCell(row, d).code }}</span>
              <template v-if="compareOn && weekCell(row, d).matched">
                <span class="match-layer" :class="weekCell(row, d).sameShift ? 'match-full' : 'match-shift'" />
                <span v-if="!weekCell(row, d).rest && !weekCell(row, d).sameShift && weekCell(row, d).real" class="real-code">{{ weekCell(row, d).real }}</span>
              </template>
              <span v-if="compareOn && weekCell(row, d).diff" class="diff-layer" /><span v-if="compareOn && weekCell(row, d).diff && weekCell(row, d).realKnownRest" class="diff-text">真休</span>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 整月对比矩阵 -->
    <el-card v-if="viewMode === 'month' && !loading">
      <template #header>
        <div class="u-row-between">
          <span>整月对比（员工 × 日期）</span>
          <div class="u-row u-gap-3 u-text-sm">
            <span v-if="compareOn" class="u-row u-gap-1"><i class="dot dot-match-full" /> 交集：班次一致（或都休）</span>
            <span v-if="compareOn" class="u-row u-gap-1"><i class="dot dot-match-shift" /> 交集：都上班但班次不同</span>
            <span v-if="compareOn" class="u-row u-gap-1"><i class="dot dot-diff" /> 这一天对不上：真实休/算法上班 或 真实上班/算法休</span>
            <span v-if="compareOn">小字 = 真实班次；红色格右下角「真休」= 真实这天休息</span>
            <template v-if="!compareOn">
              <span class="u-row u-gap-1"><i class="dot dot-base" /> 上班</span>
              <span class="u-row u-gap-1"><i class="dot dot-base-rest" /> 休息</span>
            </template>
          </div>
        </div>
      </template>
      <div class="month-scroll">
        <table class="month-table">
          <thead>
            <tr>
              <th class="sticky-col">员工</th>
              <th v-for="d in monthDates" :key="d" :class="{ weekend: isWeekend(d) }">{{ shortLabel(d) }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="emp in monthRows" :key="emp.employeeId">
              <td class="sticky-col">
                <div>{{ emp.employeeNo }}</div>
                <div class="u-text-sm u-text-hint">{{ emp.employeeName }} · {{ emp.department }}</div>
              </td>
              <td v-for="d in monthDates" :key="d" class="cell" :class="[monthCell(emp, d).baseCls, { weekend: isWeekend(d) }]">
                <span v-if="monthCell(emp, d).rest" class="base-rest-text">休</span>
                <span v-else class="base-code">{{ monthCell(emp, d).code }}</span>
                <template v-if="compareOn && monthCell(emp, d).matched">
                  <span class="match-layer" :class="monthCell(emp, d).sameShift ? 'match-full' : 'match-shift'" />
                  <span v-if="!monthCell(emp, d).rest && !monthCell(emp, d).sameShift && monthCell(emp, d).real" class="real-code">{{ monthCell(emp, d).real }}</span>
                </template>
                <span v-if="compareOn && monthCell(emp, d).diff" class="diff-layer" /><span v-if="compareOn && monthCell(emp, d).diff && monthCell(emp, d).realKnownRest" class="diff-text">真休</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-card>

    <!-- 日明细（工作站 × 时间矩阵） -->
    <el-card v-if="viewMode === 'day'">
      <template #header>
        <div class="u-row-between">
          <span>日排班明细（工作站 × 时间矩阵）</span>
          <div class="u-row u-gap-3">
            <el-date-picker v-model="workDate" type="date" value-format="YYYY-MM-DD" style="width: 150px" @change="loadDay" />
            <el-button type="primary" @click="loadDay">查询</el-button>
          </div>
        </div>
      </template>
      <div v-loading="loadingDay">
        <el-table :data="dayGrouped" border size="small">
          <el-table-column prop="workstation" label="工作站" min-width="110" show-overflow-tooltip />
          <el-table-column label="排班人员（该日该工作站）" show-overflow-tooltip>
            <template #default="{ row }">
              <el-tag v-for="emp in row.employees" :key="emp.employeeId" size="small" class="u-mr-2 u-mb-1"
                :type="emp.diff === 'diff' ? 'danger' : (emp.diff === 'shift' ? 'warning' : 'success')">
                {{ emp.employeeName }} {{ emp.shiftCode }} {{ fmtTime(emp.startTime) }}-{{ fmtTime(emp.endTime) }}
                <span v-if="emp.diff && compareOn">{{ emp.diff === 'diff' ? '（真实:休）' : emp.diff === 'shift' ? '（班次不同）' : '（一致）' }}</span>
              </el-tag>
              <span v-if="!row.employees.length" class="u-text-hint">无排班</span>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </el-card>

    <!-- 生成排班对话框 -->
    <el-dialog v-model="genVisible" title="生成 / 重排排班" width="460px">
      <el-form label-width="90px">
        <el-form-item label="开始日期">
          <el-date-picker v-model="genStart" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="结束日期">
          <el-date-picker v-model="genEnd" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="计划名称">
          <el-input v-model="genName" placeholder="留空则自动命名" />
        </el-form-item>
      </el-form>
      <el-alert type="info" :closable="false" class="u-mb-3"
        title="同一周期已有算法计划时会替换旧草稿；已发布的算法计划会拒绝重复生成。真实班表（Excel 导入）不受影响。" />
      <template #footer>
        <el-button @click="genVisible = false">取消</el-button>
        <el-button type="primary" :loading="generating" @click="doGenerate">生成</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { MagicStick } from '@element-plus/icons-vue'
import {
  getSchedules, generateSchedule, getWeekView, getMonthView, getDailyView, compareSchedule
} from '../api/schedules'

const plans = ref([])
const planId = ref(null)
const loading = ref(false)
const loadingDay = ref(false)
const generating = ref(false)
const viewMode = ref('month')
const compareOn = ref(true)
const weekStart = ref('2026-09-01')
const workDate = ref('2026-09-01')
const genVisible = ref(false)
const genStart = ref('2026-09-01')
const genEnd = ref('2026-09-30')
const genName = ref('')

const weekData = reactive({})
const monthData = reactive({})
const dayData = reactive({})
const compare = ref(null)

function fmtTime(t) {
  if (!t) return '--'
  return String(t).slice(0, 5)
}
function shortLabel(d) {
  const dt = new Date(d + 'T00:00:00')
  const wd = ['日', '一', '二', '三', '四', '五', '六'][dt.getDay()]
  return Number(String(d).slice(8, 10)) + '/' + wd
}
function isWeekend(d) {
  const day = new Date(d + 'T00:00:00').getDay()
  return day === 0 || day === 6
}
function dateOf(d) {
  const dt = new Date(d + 'T00:00:00')
  const y = dt.getFullYear()
  const m = String(dt.getMonth() + 1).padStart(2, '0')
  const dd = String(dt.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + dd
}

async function loadPlans() {
  try {
    const res = await getSchedules({ page: 1, pageSize: 100 })
    plans.value = res?.items || []
    if (!planId.value && plans.value.length) {
      const algo = plans.value.filter(p => p.source !== 'REAL')[0]
      planId.value = (algo || plans.value[0]).id
    }
    if (planId.value && !plans.value.some(p => p.id === planId.value)) {
      planId.value = plans.value[0]?.id || null
    }
  } catch (e) {
    ElMessage.error('加载排班计划失败：' + (e?.message || e))
  }
}

function currentPlan() {
  return plans.value.find(p => p.id === planId.value)
}

function realPlan() {
  return plans.value.find(p => p.source === 'REAL') || null
}

async function onPlanChange() {
  await reload()
}

async function loadCompare() {
  compare.value = null
  if (!compareOn.value || !planId.value) return
  try {
    compare.value = await compareSchedule(planId.value, realPlan()?.id || undefined)
  } catch (e) {
    ElMessage.warning('对比数据获取失败：' + (e?.message || e))
  }
}

const weekDates = computed(() => {
  const out = []
  const d = new Date(weekStart.value + 'T00:00:00')
  for (let i = 0; i < 7; i++) {
    const x = new Date(d)
    x.setDate(d.getDate() + i)
    const y = x.getFullYear()
    const m = String(x.getMonth() + 1).padStart(2, '0')
    const dd = String(x.getDate()).padStart(2, '0')
    out.push(y + '-' + m + '-' + dd)
  }
  return out
})
const weekRows = computed(() => weekData.rows || [])
function weekCell(row, d) {
  const day = (row.days || []).find(x => x.workDate === d)
  const rest = !day || day.isRestDay === 1
  let matched = false
  let sameShift = false
  let real = null
  let diff = false
  let realKnownRest = false
  if (compareOn.value && compare.value) {
    const c = compare.value.cells.find(x => x.employeeId === row.employeeId && x.workDate === d)
    if (c) {
      // 交集：两边上班/休息一致才点绿；班次同=深绿、班次异=浅绿
      matched = c.state === 'SAME_REST' || c.state === 'SAME_WORK' || c.state === 'SHIFT_DIFF'
      sameShift = c.state === 'SAME_REST' || c.state === 'SAME_WORK'
      real = c.realRest ? null : c.realShift
      diff = c.state === 'REAL_REST_ALGO_WORK' || c.state === 'REAL_WORK_ALGO_REST'
      realKnownRest = c.realRest
    }
  }
  return {
    rest,
    baseCls: rest ? 'base-rest' : 'base-work',
    code: rest ? '休' : (day.shiftCode || '班'),
    matched,
    sameShift,
    real,
    diff,
    realKnownRest
  }
}
async function loadWeek() {
  if (!planId.value) return
  loading.value = true
  try {
    weekData.rows = (await getWeekView(planId.value, weekStart.value)) || []
    await loadCompare()
  } catch (e) {
    ElMessage.error('加载周视图失败：' + (e?.message || e))
  } finally {
    loading.value = false
  }
}

const monthDates = computed(() => {
  const plan = currentPlan()
  const start = plan?.startDate || '2026-09-01'
  const end = plan?.endDate || '2026-09-30'
  const out = []
  let d = new Date(start + 'T00:00:00')
  const e = new Date(end + 'T00:00:00')
  while (d <= e) {
    const y = d.getFullYear()
    const m = String(d.getMonth() + 1).padStart(2, '0')
    const dd = String(d.getDate()).padStart(2, '0')
    out.push(y + '-' + m + '-' + dd)
    d = new Date(d)
    d.setDate(d.getDate() + 1)
  }
  return out
})
const monthRows = computed(() => monthData.rows || [])
function monthCell(emp, d) {
  const day = (emp.days || []).find(x => x.workDate === d)
  const rest = !day || day.isRestDay === 1
  let matched = false
  let sameShift = false
  let real = null
  let diff = false
  let realKnownRest = false
  if (compareOn.value && compare.value) {
    const c = compare.value.cells.find(x => x.employeeId === emp.employeeId && x.workDate === d)
    if (c) {
      matched = c.state === 'SAME_REST' || c.state === 'SAME_WORK' || c.state === 'SHIFT_DIFF'
      sameShift = c.state === 'SAME_REST' || c.state === 'SAME_WORK'
      real = c.realRest ? null : c.realShift
      diff = c.state === 'REAL_REST_ALGO_WORK' || c.state === 'REAL_WORK_ALGO_REST'
      realKnownRest = c.realRest
    }
  }
  return {
    rest,
    baseCls: rest ? 'base-rest' : 'base-work',
    code: rest ? '休' : (day.shiftCode || '班'),
    matched,
    sameShift,
    real,
    diff,
    realKnownRest
  }
}
async function loadMonth() {
  if (!planId.value) return
  loading.value = true
  try {
    monthData.rows = (await getMonthView(planId.value)) || []
    await loadCompare()
  } catch (e) {
    ElMessage.error('加载月视图失败：' + (e?.message || e))
  } finally {
    loading.value = false
  }
}

const dayGrouped = computed(() => dayData.grouped || [])
async function loadDay() {
  if (!planId.value) return
  loadingDay.value = true
  try {
    const data = await getDailyView(planId.value, workDate.value)
    const rows = data?.rows || []
    const wsNames = [...new Set(rows.map(r => r.workstationName || '未分配'))].sort()
    const grouped = wsNames.map(ws => {
      const employees = rows.filter(r => (r.workstationName || '未分配') === ws)
      const uniq = []
      const seen = new Set()
      for (const r of employees) {
        const key = r.employeeId
        if (seen.has(key)) continue
        seen.add(key)
        let diff = null
        if (compareOn.value && compare.value) {
          const c = compare.value.cells.find(x => x.employeeId === r.employeeId && x.workDate === workDate.value)
          if (c) {
            diff = c.state === 'SAME_WORK' || c.state === 'SAME_REST' ? 'same' : (c.state === 'SHIFT_DIFF' ? 'shift' : 'diff')
          }
        }
        uniq.push({ ...r, diff })
      }
      return { workstation: ws, employees: uniq }
    })
    dayData.grouped = grouped
    await loadCompare()
  } catch (e) {
    ElMessage.error('加载日明细失败：' + (e?.message || e))
  } finally {
    loadingDay.value = false
  }
}

function openGenerate() {
  const plan = currentPlan()
  if (plan) {
    genStart.value = plan.startDate
    genEnd.value = plan.endDate
  }
  genName.value = ''
  genVisible.value = true
}
async function doGenerate() {
  if (!genStart.value || !genEnd.value) {
    ElMessage.warning('请选择开始和结束日期')
    return
  }
  generating.value = true
  try {
    const res = await generateSchedule({
      startDate: genStart.value,
      endDate: genEnd.value,
      planName: genName.value || undefined
    })
    ElMessage.success('生成成功：' + (res?.planName || '') + '（问题 ' + (res?.issueCount ?? 0) + ' 条）')
    genVisible.value = false
    await loadPlans()
    planId.value = res?.planId || planId.value
    await reload()
  } catch (e) {
    ElMessage.error('生成失败：' + (e?.message || e))
  } finally {
    generating.value = false
  }
}

async function reload() {
  if (viewMode.value === 'week') await loadWeek()
  else if (viewMode.value === 'month') await loadMonth()
  else if (viewMode.value === 'day') await loadDay()
}

onMounted(async () => {
  await loadPlans()
  await reload()
})
</script>

<style scoped>
.stat-card {
  min-width: 200px;
  flex: 1 1 200px;
}
.stat-num {
  font-size: var(--app-font-xl);
  font-weight: 700;
  color: var(--app-brand);
}
.stat-label {
  font-size: var(--app-font-xs);
  color: var(--el-text-color-secondary);
  margin-top: var(--app-space-2);
}
.month-scroll {
  overflow: auto;
  max-height: 70vh;
}
.month-table {
  border-collapse: separate;
  border-spacing: 0;
  font-size: var(--app-font-xs);
}
.month-table th, .month-table td {
  border: 1px solid var(--el-border-color-lighter);
  padding: var(--app-space-2) var(--app-space-3);
  text-align: center;
  white-space: nowrap;
}
.month-table th {
  background: var(--el-fill-color-light);
  position: sticky;
  top: 0;
  z-index: 2;
}
.month-table th.weekend, .month-table td.weekend {
  background: var(--el-color-primary-light-9);
}
.sticky-col {
  position: sticky;
  left: 0;
  z-index: 1;
  background: var(--el-bg-color);
  text-align: left !important;
}
.month-table td.cell {
  position: relative;
}
/* 算法排班底色：上班=纯白，休息=浅灰 */
.base-work { background: var(--el-bg-color); }
.base-rest { background: var(--el-fill-color-light); }
.base-rest-text { color: var(--el-text-color-secondary); }
.base-code { font-weight: 600; }

/* 交集蒙板：算法与真实一致的格子点绿；不一致保持白色 */
.cell-wrap { position: relative; padding: 2px 4px; border-radius: var(--app-radius-sm); }
.match-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
  border-radius: inherit;
}
.match-full {
  background: var(--el-color-primary-light-7);
  opacity: 0.9;
}
.match-shift {
  background: var(--el-color-success-light-7);
  opacity: 0.8;
}
.diff-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
  border-radius: inherit;
  background: var(--el-color-danger-light-8);
  opacity: 0.7;
}
.diff-text {
  position: absolute;
  right: 2px;
  bottom: 0;
  z-index: 1;
  font-size: 10px;
  line-height: 1;
  color: var(--el-color-danger);
  font-weight: 700;
}
.real-code {
  position: absolute;
  right: 2px;
  bottom: 0;
  z-index: 1;
  font-size: 10px;
  line-height: 1;
  color: var(--el-color-success-dark-2);
  font-weight: 700;
}

.dot {
  display: inline-block;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  margin-right: var(--app-space-1);
  border: 1px solid var(--el-border-color);
}
.dot-base { background: var(--el-bg-color); }
.dot-base-rest { background: var(--el-fill-color-light); }
.dot-match-full { background: var(--el-color-primary-light-7); }
.dot-match-shift { background: var(--el-color-success-light-7); }
.dot-diff { background: var(--el-color-danger-light-8); }
.u-flex-1 { flex: 1; }
</style>
