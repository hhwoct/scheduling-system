<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: var(--app-space-4)">
          <span>排班报表</span>
          <div style="display: flex; align-items: center; gap: var(--app-space-4)">
            <el-select
              v-model="planId"
              placeholder="请选择已发布的排班计划"
              style="width: 360px"
              :disabled="!plans.length"
              @change="loadData"
            >
              <el-option
                v-for="p in plans"
                :key="p.id"
                :label="`${p.planName}（${p.startDate} ~ ${p.endDate}）`"
                :value="p.id"
              />
            </el-select>
            <el-button type="primary" :loading="loading" :disabled="!planId" @click="loadData">刷新</el-button>
            <el-button v-if="plans.length" type="success" :loading="exporting" @click="exportCsv">导出 CSV</el-button>
          </div>
        </div>
      </template>

      <!-- 没有任何已发布的排班安排 -->
      <el-empty v-if="!plansLoading && !plans.length" description="暂未发布排班安排，请发布后再查看" />

      <template v-else>
        <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: var(--app-space-5)" />

        <div v-loading="loading || plansLoading">
          <template v-if="rows.length">
            <!-- 员工 × 日期 排班表（样式同「排班查看 · 周视图」） -->
            <div class="gantt">
              <div class="gantt-row gantt-header">
                <div class="gantt-emp-col">员工</div>
                <div v-for="d in dayList" :key="d.date" class="gantt-day-col" :class="{ weekend: d.weekend }">
                  <div class="day-label" :class="{ 'day-weekend': d.weekend }">{{ d.weekday }}</div>
                  <div class="day-sub">{{ d.date.slice(5) }}</div>
                </div>
              </div>

              <template v-for="(row, i) in rows" :key="row.employeeId">
                <!-- 全职员工与兼职员工之间的分隔行 -->
                <div v-if="i === firstPartTimeIndex" class="gantt-divider">
                  <span class="gantt-divider-badge">兼</span>兼职员工
                </div>
                <div class="gantt-row" :class="{ 'gantt-row-parttime': row.isParttime === 1 }">
                  <div class="gantt-emp-col">
                    <div class="emp-name">
                      {{ row.employeeName }}
                      <el-tag v-if="row.isParttime === 1" type="success" size="small" style="margin-left: var(--app-space-2)">兼</el-tag>
                    </div>
                    <div class="emp-sub">{{ row.department }} · {{ row.employeeNo }}</div>
                  </div>
                  <div v-for="d in dayList" :key="d.date" class="gantt-day-col" :class="{ weekend: d.weekend }">
                    <template v-if="getDay(row, d.date)">
                      <div v-if="getDay(row, d.date).isRestDay === 1 && row.isParttime !== 1" class="day-block rest-block">休</div>
                      <div v-else-if="getDay(row, d.date).isRestDay === 1" class="day-block empty-block"></div>
                      <div v-else class="day-block work-block" :title="workTip(getDay(row, d.date))">
                        <div class="shift-code">{{ getDay(row, d.date).shiftCode || '班' }}</div>
                        <div class="shift-time">{{ fmt(getDay(row, d.date).startTime) }}-{{ fmt(getDay(row, d.date).endTime) }}</div>
                        <div class="shift-hours">{{ Number(getDay(row, d.date).workHours || 0).toFixed(1) }}h</div>
                        <div
                          v-if="getDay(row, d.date).breakStartTime && row.isParttime !== 1"
                          class="shift-break"
                          :title="getDay(row, d.date).breakCoverEmployeeName
                            ? `休息 ${fmt(getDay(row, d.date).breakStartTime)}-${fmt(getDay(row, d.date).breakEndTime)}，由 ${getDay(row, d.date).breakCoverEmployeeName} 顶班`
                            : `休息 ${fmt(getDay(row, d.date).breakStartTime)}-${fmt(getDay(row, d.date).breakEndTime)}`"
                        >
                          休 {{ fmt(getDay(row, d.date).breakStartTime) }}-{{ fmt(getDay(row, d.date).breakEndTime) }}{{ getDay(row, d.date).breakCoverEmployeeName ? ' · ' + getDay(row, d.date).breakCoverEmployeeName + ' 顶' : '' }}
                        </div>
                      </div>
                    </template>
                    <div v-else class="day-block empty-block"></div>
                  </div>
                </div>
              </template>
            </div>

            <div class="legend">
              <span class="legend-item"><span class="legend-box rest-legend-box">休</span>休息</span>
              <span class="legend-item"><span class="legend-box work-legend-box"></span>上班班次（班次 / 时间 / 工时）</span>
              <span class="legend-item"><span class="legend-box break-legend-box">休</span>班中休息（含顶班人）</span>
              <span class="legend-item"><span class="legend-box pt-legend-box">兼</span>兼职员工（休息日留空）</span>
              <span class="legend-item legend-hint">悬停班次色块可查看班次、工时、工作站与顶班详情</span>
            </div>
          </template>

          <el-empty v-else-if="!loading && !errorMsg" description="该排班计划暂无排班数据" />
        </div>
      </template>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { getSchedules, getWeekView } from '../api/schedules'
import { getWorkstations } from '../api/workstations'

const planId = ref('')
const plans = ref([])
const plansLoading = ref(false)
const loading = ref(false)
const exporting = ref(false)
const errorMsg = ref('')
const rawRows = ref([])
const wsMap = ref({})

const weekdayNames = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']

function fmtDate(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function fmt(t) { return t ? String(t).substring(0, 5) : '--' }
function wsNames(covered) {
  return String(covered || '').split(',').map(id => wsMap.value[id]).filter(Boolean)
}

const currentPlan = computed(() => plans.value.find(p => p.id === planId.value) || null)

// 计划周期内的每一天
const dayList = computed(() => {
  const plan = currentPlan.value
  if (!plan) return []
  const list = []
  const start = new Date(plan.startDate + 'T00:00:00')
  const end = new Date(plan.endDate + 'T00:00:00')
  for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
    list.push({
      date: fmtDate(d),
      weekday: weekdayNames[(d.getDay() + 6) % 7],
      weekend: d.getDay() === 0 || d.getDay() === 6
    })
  }
  return list
})

function getDay(row, date) { return row.dayMap[date] }

// 兼职员工整个排班周期一天班都没上的（全部休息/无排班），不展示也不导出
const filteredRows = computed(() => (rawRows.value || []).filter(r => {
  if (Number(r.isParttime) !== 1) return true
  return dayList.value.some(d => {
    const day = r.dayMap[d.date]
    return !!day && day.isRestDay === 0
  })
}))

// 全职在前、兼职在后（同组按工号），供分隔行渲染
const rows = computed(() => {
  const list = [...(filteredRows.value || [])]
  list.sort((a, b) => (Number(a.isParttime) - Number(b.isParttime)) || String(a.employeeNo || '').localeCompare(String(b.employeeNo || '')))
  return list
})
const firstPartTimeIndex = computed(() => rows.value.findIndex(r => Number(r.isParttime) === 1))

// 班次色块悬停提示：班次、时间、工时、工作站、班中休息与顶班人
function workTip(d) {
  const lines = [
    `${d.shiftCode || '班次'} ${fmt(d.startTime)} - ${fmt(d.endTime)}`,
    `工时 ${Number(d.workHours || 0).toFixed(1)}h`
  ]
  const wss = wsNames(d.coveredWorkstations)
  if (wss.length) lines.push(`工作站：${wss.join('、')}`)
  if (d.breakStartTime) {
    lines.push(`班中休息 ${fmt(d.breakStartTime)} - ${fmt(d.breakEndTime)}${d.breakCoverEmployeeName ? `（${d.breakCoverEmployeeName} 顶班）` : ''}`)
  }
  return lines.join('\n')
}

async function loadPlans() {
  plansLoading.value = true
  try {
    const res = await getSchedules({ page: 1, pageSize: 100, status: 'PUBLISHED' })
    plans.value = res.items || []
    if (plans.value.length) {
      planId.value = plans.value[0].id
      await loadData()
    }
  } finally {
    plansLoading.value = false
  }
}

async function loadData() {
  if (!planId.value) return
  loading.value = true
  errorMsg.value = ''
  try {
    const plan = currentPlan.value
    if (!plan) {
      rawRows.value = []
      return
    }
    // 计划周期拆成多个自然周（周视图接口按 7 天切片），并行拉取后合并
    const weeks = []
    const end = new Date(plan.endDate + 'T00:00:00')
    for (let d = new Date(plan.startDate + 'T00:00:00'); d <= end; d.setDate(d.getDate() + 7)) {
      weeks.push(fmtDate(d))
    }
    const weekResults = await Promise.all(weeks.map(w => getWeekView(planId.value, w)))

    const empMap = new Map()
    for (const week of weekResults) {
      for (const row of week || []) {
        let entry = empMap.get(row.employeeId)
        if (!entry) {
          entry = {
            employeeId: row.employeeId,
            employeeNo: row.employeeNo,
            employeeName: row.employeeName,
            department: row.department,
            isParttime: row.isParttime,
            dayMap: {}
          }
          empMap.set(row.employeeId, entry)
        }
        for (const d of row.days || []) {
          if (d && d.workDate) entry.dayMap[d.workDate] = d
        }
      }
    }
    rawRows.value = Array.from(empMap.values())
  } catch (e) {
    rawRows.value = []
    errorMsg.value = '加载排班数据失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

// CSV 注入防护：转义逗号/引号/换行，并中性化 Excel 公式前缀(=, +, -, @)
function csvEscape(value) {
  let v = String(value ?? '')
  if (/^\s*[=+\-@\t\r]/.test(v)) {
    v = "'" + v
  }
  if (/[",\n\r]/.test(v)) {
    v = '"' + v.replace(/"/g, '""') + '"'
  }
  return v
}

async function exportCsv() {
  exporting.value = true
  try {
    const headers = ['工号', '姓名', '部门', '日期', '状态', '班次', '时间', '工时', '工作站', '班中休息'].map(csvEscape).join(',')
    const csv = [headers]
    for (const row of filteredRows.value) {
      const days = Object.values(row.dayMap)
      days.sort((a, b) => (a.workDate < b.workDate ? -1 : a.workDate > b.workDate ? 1 : 0))
      for (const d of days) {
        const work = d.isRestDay !== 1
        csv.push([
          csvEscape(row.employeeNo),
          csvEscape(row.employeeName),
          csvEscape(row.department),
          csvEscape(d.workDate),
          csvEscape(work ? '上班' : '休息'),
          csvEscape(d.shiftCode || ''),
          csvEscape(work ? `${fmt(d.startTime)}-${fmt(d.endTime)}` : ''),
          csvEscape(work ? Number(d.workHours || 0).toFixed(1) : ''),
          csvEscape(wsNames(d.coveredWorkstations).join('、')),
          csvEscape(d.breakStartTime ? `${fmt(d.breakStartTime)}-${fmt(d.breakEndTime)}${d.breakCoverEmployeeName ? `（${d.breakCoverEmployeeName} 顶班）` : ''}` : '')
        ].join(','))
      }
    }
    const blob = new Blob(['\uFEFF' + csv.join('\n')], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const planName = currentPlan.value ? currentPlan.value.planName : planId.value
    a.download = `排班报表_${planName}_${new Date().toISOString().slice(0, 10)}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    setTimeout(() => {
      URL.revokeObjectURL(url)
    }, 1000)
    ElMessage.success('导出成功')
  } finally {
    exporting.value = false
  }
}

onMounted(async () => {
  loadPlans()
  try {
    const list = await getWorkstations()
    const map = {}
    for (const w of list || []) map[w.id] = w.name
    wsMap.value = map
  } catch {
    // 工作站映射失败不影响排班表主体展示
  }
})
</script>

<style scoped>
.gantt { border: 1px solid var(--el-border-color-lighter); border-radius: var(--app-radius-sm); overflow-x: auto; }
.gantt-row { display: flex; border-bottom: 1px solid var(--el-border-color-lighter); min-width: 100%; }
.gantt-row:last-child { border-bottom: none; }
.gantt-row-parttime .gantt-emp-col { background: var(--app-parttime-bg); }
.gantt-divider {
  background: var(--el-color-success-light-9);
  color: var(--el-color-success);
  font-weight: 600;
  font-size: var(--app-font-sm);
  padding: var(--app-space-3) 10px;
  border-bottom: 1px solid var(--app-parttime-border);
  display: flex;
  align-items: center;
  gap: var(--app-space-3);
  position: sticky;
  left: 0;
}
.gantt-divider-badge { display: inline-block; background: var(--el-color-success); color: var(--el-color-white); border-radius: var(--app-radius-sm); font-size: var(--app-font-xs); padding: 0 var(--app-space-3); line-height: 16px; }
.gantt-header { background: var(--el-fill-color-light); font-weight: 600; position: sticky; top: 0; z-index: 4; }
.gantt-emp-col {
  width: 150px;
  flex-shrink: 0;
  padding: var(--app-space-3) var(--app-space-4);
  border-right: 1px solid var(--el-border-color-lighter);
  position: sticky;
  left: 0;
  background: var(--el-bg-color);
  z-index: 3;
  display: flex;
  flex-direction: column;
  justify-content: center;
}
.gantt-header .gantt-emp-col { background: var(--el-fill-color-light); z-index: 5; }
.gantt-day-col { width: 160px; flex-shrink: 0; padding: var(--app-space-2); border-right: 1px solid var(--el-border-color-lighter); box-sizing: border-box; }
.gantt-day-col:last-child { border-right: none; }
.gantt-day-col.weekend { background-color: var(--el-fill-color-lighter); }
.day-label { font-size: var(--app-font-sm); color: var(--el-text-color-regular); text-align: center; }
.day-label.day-weekend { color: var(--el-color-warning); }
.day-sub { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); text-align: center; }
.day-block { border-radius: var(--app-radius-sm); padding: var(--app-space-3); text-align: center; font-size: var(--app-font-sm); height: 80px; box-sizing: border-box; overflow: hidden; }
.work-block { background: var(--el-color-primary-light-9); display: flex; flex-direction: column; align-items: center; justify-content: center; }
.rest-block { background: var(--el-color-danger-light-9); color: var(--el-color-danger); font-weight: 600; display: flex; align-items: center; justify-content: center; }
.empty-block { background: var(--el-fill-color-lighter); }
.shift-code { font-weight: 600; }
.shift-time { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); }
.shift-hours { font-size: var(--app-font-xs); color: var(--el-color-primary-light-3); }
.shift-break { margin-top: var(--app-space-1); font-size: var(--app-font-micro); color: var(--el-color-warning); line-height: 1.4; }
.emp-name { font-size: var(--app-font-sm); font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.emp-sub { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

.legend { margin-top: var(--app-space-5); display: flex; gap: var(--app-space-6); align-items: center; flex-wrap: wrap; }
.legend-item { display: inline-flex; align-items: center; gap: var(--app-space-3); font-size: var(--app-font-sm); color: var(--el-text-color-regular); }
.legend-box { display: inline-flex; align-items: center; justify-content: center; width: 16px; height: 16px; border-radius: var(--app-radius-sm); font-size: var(--app-font-micro); }
.rest-legend-box { background: var(--el-color-danger-light-9); color: var(--el-color-danger); font-weight: 600; }
.work-legend-box { background: var(--el-color-primary-light-9); }
.break-legend-box { background: var(--el-color-warning-light-9); color: var(--el-color-warning); }
.pt-legend-box { background: var(--el-color-success-light-9); color: var(--el-color-success); }
.legend-hint { color: var(--el-text-color-secondary); }
</style>
