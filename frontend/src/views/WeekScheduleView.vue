<template>
  <div>
    <el-card>
      <template #header>
        <div class="u-row-between">
          <span>周排班视图（资源时间线）</span>
          <div>
            <el-input class="u-mr-4" v-model="planId" placeholder="排班计划 ID" style="width: 180px" />
            <el-date-picker class="u-mr-4" v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px" placeholder="周起始日" />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert class="u-mb-5" v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" />

      <!-- 整周期级问题（无具体日期，如工时超限） -->
      <el-alert class="u-mb-5"
        v-if="summaryIssues.length"
        type="warning"
        :closable="false"
       
      >
        <template #title>
          整周期问题（{{ summaryIssues.length }} 条）
        </template>
        <div class="u-text-sm u-mt-1" v-for="(si, idx) in summaryIssues.slice(0, 5)" :key="idx">
          {{ si.description }}
        </div>
        <span class="u-text-sm" v-if="summaryIssues.length > 5">… 等 {{ summaryIssues.length }} 条</span>

        <el-button class="u-mt-4"
          v-if="summaryIssues.length"
          type="primary"
          size="small"
         
          @click="showOvertimeDetail = true"
        >
          查看详情
        </el-button>
      </el-alert>

      <div v-loading="loading" class="gantt-wrap">
        <div class="gantt">
          <!-- 表头：7 天 -->
          <div class="gantt-row gantt-header">
            <div class="gantt-emp-col">员工</div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <div class="day-label">{{ weekdayName(d.weekday) }}</div>
              <div class="day-sub">{{ d.date }}</div>
            </div>
          </div>

          <!-- 员工行：每天显示班次时间块 -->
          <div v-for="row in rows" :key="row.employeeId" class="gantt-row">
            <div class="gantt-emp-col">
              <div class="emp-name">{{ row.employeeName }}</div>
              <div class="emp-sub">{{ row.department }} · {{ row.employeeNo }}</div>
            </div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <template v-if="getDay(row, d.date)">
                <div v-if="getDay(row, d.date).isRestDay === 1 && row.isParttime !== 1" class="day-block rest-block" :class="dayIssuesClass(row, d.date)">
                  休<div v-if="dayIssues(row, d.date).length" class="issue-badge">{{ dayIssueText(row, d.date) }}</div>
                </div>
                <div v-else-if="getDay(row, d.date).isRestDay === 1" class="day-block empty-block"></div>
                <div v-else class="day-block work-block" :class="dayIssuesClass(row, d.date)">
                  <div class="shift-code">{{ getDay(row, d.date).shiftCode || '班' }}</div>
                  <div class="shift-time">{{ fmt(getDay(row, d.date).startTime) }}-{{ fmt(getDay(row, d.date).endTime) }}</div>
                  <div class="shift-hours">{{ getDay(row, d.date).workHours }}h</div>
                  <div
                    v-if="getDay(row, d.date).breakStartTime && row.isParttime !== 1"
                    class="shift-break"
                    :title="getDay(row, d.date).breakCoverEmployeeName
                      ? `休息 ${fmt(getDay(row, d.date).breakStartTime)}-${fmt(getDay(row, d.date).breakEndTime)}，由 ${getDay(row, d.date).breakCoverEmployeeName} 顶班`
                      : `休息 ${fmt(getDay(row, d.date).breakStartTime)}-${fmt(getDay(row, d.date).breakEndTime)}`"
                  >
                    休 {{ fmt(getDay(row, d.date).breakStartTime) }}-{{ fmt(getDay(row, d.date).breakEndTime) }}{{ getDay(row, d.date).breakCoverEmployeeName ? ' · ' + getDay(row, d.date).breakCoverEmployeeName + ' 顶' : '' }}
                  </div>
                  <div v-if="dayIssues(row, d.date).length" class="issue-badge">连续</div>
                </div>
              </template>
              <div v-else class="day-block empty-block"></div>
            </div>
          </div>
        </div>
      </div>

      <div class="u-row u-mt-5 u-gap-6">
        <el-tag size="small" type="danger">休</el-tag><span class="u-text-hint">休息</span>
        <el-tag size="small" type="primary">班</el-tag><span class="u-text-hint">班次（含时间与工时）</span>
        <el-tag size="small" type="warning">休 HH:mm-HH:mm</el-tag><span class="u-text-hint">班中休息（含顶岗人）</span>
      </div>
    </el-card>

    <!-- 整周期问题详情弹窗 -->
    <el-dialog v-model="showOvertimeDetail" title="整周期问题详情（工时超限）" width="760px">
      <el-alert class="u-mb-5" type="info" :closable="false">
        以下员工排班周期内总工时超过上限（58h）。点击「工时明细」可查看该员工每天的班次与工时，定位超时来源。
      </el-alert>
      <el-table :data="summaryIssues" border stripe size="small" max-height="440">
        <el-table-column type="expand">
          <template #default="{ row }">
            <div style="padding: var(--app-space-4) var(--app-space-6)">
              <el-table :data="employeeHourRows[`${planId}_${row.employeeId}`] || []" size="small" border max-height="260">
                <el-table-column prop="workDate" label="日期" width="110" />
                <el-table-column label="类型" width="60">
                  <template #default="{ row: d }">{{ d.isRestDay === 1 ? '休' : '班' }}</template>
                </el-table-column>
                <el-table-column prop="shiftCode" label="班次" width="80" />
                <el-table-column label="时长(h)" width="90">
                  <template #default="{ row: d }">{{ Number(d.workHours).toFixed(1) }}</template>
                </el-table-column>
              </el-table>
              <div class="u-text-hint" v-if="!((employeeHourRows[`${planId}_${row.employeeId}`] || []).length)">（点击右侧「工时明细」加载）</div>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="description" label="问题描述" min-width="300" />
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button link type="primary" @click="loadEmployeeHours(row.employeeId)">工时明细</el-button>
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <el-button @click="showOvertimeDetail = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getWeekView, getScheduleIssues, getMonthView } from '../api/schedules'

const route = useRoute()
const planId = ref(route.query.planId || '')
const weekStart = ref('')
const loading = ref(false)
const rows = ref([])
const weekDays = ref([])
const issues = ref([])
const errorMsg = ref('')
const showOvertimeDetail = ref(false)
const employeeHourRows = ref({})
const loadingSet = new Set()  // 追踪加载状态，防止重复加载

const summaryIssues = computed(() => issues.value.filter(i => i.workDate == null))

const weekdayNames = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']
function weekdayName(i) { return weekdayNames[i] }

function fmt(t) { return t ? String(t).substring(0, 5) : '--' }

// 返回该员工该日期关联的员工个人问题。
// 岗位缺口(STAFFING_GAP)属于工作站级，在日排班明细的矩阵里按工作站×时段展示；周视图只标记员工个人问题。
function dayIssues(row, date) {
  return issues.value.filter(i =>
    (i.issueType === 'CONSECUTIVE_WORK' || i.issueType === 'OVERTIME') &&
    i.workDate === date &&
    i.employeeId === row.employeeId
  )
}
function dayIssuesClass(row, date) {
  const list = dayIssues(row, date)
  if (!list.length) return ''
  if (list.some(i => i.issueType === 'CONSECUTIVE_WORK')) return 'has-issue-person'
  return ''
}
function dayIssueText(row, date) {
  const list = dayIssues(row, date)
  if (!list.length) return ''
  return list.some(i => i.issueType === 'OVERTIME') ? '超时' : '连续'
}

function getDay(row, date) { return row.days.find(d => d.workDate === date) }

function buildWeekDays() {
  if (!weekStart.value) {
    weekDays.value = []
    return
  }
  const start = new Date(weekStart.value + 'T00:00:00')
  const days = []
  for (let i = 0; i < 7; i++) {
    const d = new Date(start)
    d.setDate(start.getDate() + i)
    days.push({
      date: `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`,
      weekday: (d.getDay() + 6) % 7
    })
  }
  weekDays.value = days
}

// P3-13/P3-14: 按 planId 缓存，空结果也缓存（区分未加载），失败不缓存允许重试
async function loadEmployeeHours(employeeId) {
  const cacheKey = `${planId.value}_${employeeId}`

  // 已加载过（包含空结果 null/[]）直接返回
  if (employeeHourRows.value[cacheKey] !== undefined) {
    return employeeHourRows.value[cacheKey]
  }

  // 防止重复加载
  if (loadingSet.has(cacheKey)) return
  loadingSet.add(cacheKey)

  try {
    const month = await getMonthView(planId.value)
    const emp = month.find(e => e.employeeId === employeeId)
    // 空结果也缓存为 []，避免重复请求；用 null 表示未加载
    employeeHourRows.value[cacheKey] = emp ? emp.days : []
    return employeeHourRows.value[cacheKey]
  } catch (e) {
    // 失败时不缓存，允许重试
    ElMessage.error('加载工时明细失败')
    throw e
  } finally {
    loadingSet.delete(cacheKey)
  }
}

async function loadData() {
  if (!planId.value) {
    errorMsg.value = '请输入排班计划 ID'
    return
  }
  loading.value = true
  errorMsg.value = ''
  // P3-13: 加载新计划时清除缓存，避免数据陈旧
  employeeHourRows.value = {}
  loadingSet.clear()
  try {
    rows.value = await getWeekView(planId.value, weekStart.value || undefined)
    issues.value = await getScheduleIssues(planId.value)

    if (rows.value.length > 0 && rows.value[0].days.length > 0) {
      const dates = rows.value[0].days.map(d => d.workDate).sort()
      if (!weekStart.value) {
        weekStart.value = dates[0]
      }
    }
    buildWeekDays()
  } catch (e) {
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

// query planId 变化时重载
watch(() => route.query.planId, (val) => {
  if (val) {
    planId.value = val
    loadData()
  }
})

onMounted(loadData)
</script>

<style scoped>
.gantt-wrap { overflow-x: auto; }
.gantt { min-width: 100%; }
.gantt-row { display: flex; border-bottom: 1px solid var(--el-border-color-lighter); }
.gantt-header { background: var(--el-fill-color-light); font-weight: 600; }
.gantt-emp-col { width: 150px; flex-shrink: 0; padding: var(--app-space-3) var(--app-space-4); border-right: 1px solid var(--el-border-color-lighter); position: sticky; left: 0; background: var(--el-bg-color); z-index: 3; }
.gantt-header .gantt-emp-col { background: var(--el-fill-color-light); z-index: 4; }
.emp-name { font-size: var(--app-font-base); }
.emp-sub { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); }
.gantt-day-col { flex: 1; min-width: 120px; padding: var(--app-space-3); border-right: 1px solid var(--el-fill-color-light); }
.gantt-day-col:last-child { border-right: none; }
.day-label { font-size: var(--app-font-base); text-align: center; }
.day-sub { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); text-align: center; }
.day-block { min-height: 84px; border-radius: var(--app-radius-sm); display: flex; flex-direction: column; align-items: center; justify-content: center; padding: var(--app-space-2) var(--app-space-1); }
.shift-break { margin-top: var(--app-space-1); font-size: var(--app-font-micro); color: var(--el-color-warning); line-height: 1.4; text-align: center; }
.rest-block { background: var(--el-color-danger-light-9); color: var(--el-color-danger); font-weight: 600; font-size: var(--app-font-md); }
.work-block { background: var(--el-color-primary-light-9); color: var(--el-color-primary); }
.shift-code { font-weight: 600; font-size: var(--app-font-base); }
.shift-time { font-size: var(--app-font-xs); }
.shift-hours { font-size: var(--app-font-xs); color: var(--el-color-primary-light-3); }
.empty-block { background: var(--el-fill-color-lighter); }
.has-issue-person { box-shadow: inset 0 0 0 2px var(--el-color-danger); }
.issue-badge { margin-top: var(--app-space-1); background: var(--el-color-danger); color: var(--el-color-white); font-size: var(--app-font-micro); border-radius: var(--app-radius-sm); padding: 0 var(--app-space-2); }
</style>