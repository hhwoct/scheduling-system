<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>周排班视图（资源时间线）</span>
          <div>
            <el-input v-model="planId" placeholder="排班计划 ID" style="width: 180px; margin-right: 8px" />
            <el-date-picker v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-right: 8px" placeholder="周起始日" />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: 12px" />

      <!-- 整周期级问题（无具体日期，如工时超限） -->
      <el-alert
        v-if="summaryIssues.length"
        type="warning"
        :closable="false"
        style="margin-bottom: 12px"
      >
        <template #title>
          整周期问题（{{ summaryIssues.length }} 条）
        </template>
        <div v-for="(si, idx) in summaryIssues.slice(0, 5)" :key="idx" style="font-size: 12px; margin-top: 2px">
          {{ si.description }}
        </div>
        <span v-if="summaryIssues.length > 5" style="font-size: 12px">… 等 {{ summaryIssues.length }} 条</span>

        <el-button
          v-if="summaryIssues.length"
          type="primary"
          size="small"
          style="margin-top: 8px"
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
                <div v-if="getDay(row, d.date).isRestDay === 1" class="day-block rest-block" :class="dayIssuesClass(row, d.date)">
                  休<div v-if="dayIssues(row, d.date).length" class="issue-badge">{{ dayIssues(row, d.date)[0].issueType === 'OVERTIME' ? '超时' : '连续' }}</div>
                </div>
                <div v-else class="day-block work-block" :class="dayIssuesClass(row, d.date)">
                  <div class="shift-code">{{ getDay(row, d.date).shiftCode || '班' }}</div>
                  <div class="shift-time">{{ fmt(getDay(row, d.date).startTime) }}-{{ fmt(getDay(row, d.date).endTime) }}</div>
                  <div class="shift-hours">{{ getDay(row, d.date).workHours }}h</div>
                  <div v-if="dayIssues(row, d.date).length" class="issue-badge">连续</div>
                </div>
              </template>
              <div v-else class="day-block empty-block"></div>
            </div>
          </div>
        </div>
      </div>

      <div style="margin-top: 12px; display: flex; gap: 16px; align-items: center">
        <el-tag size="small" type="danger">休</el-tag><span style="font-size: 12px; color: #909399">休息</span>
        <el-tag size="small" type="primary">班</el-tag><span style="font-size: 12px; color: #909399">班次（含时间与工时）</span>
      </div>
    </el-card>

    <!-- 整周期问题详情弹窗 -->
    <el-dialog v-model="showOvertimeDetail" title="整周期问题详情（工时超限）" width="760px">
      <el-alert type="info" :closable="false" style="margin-bottom: 12px">
        以下员工排班周期内总工时超过上限（58h）。点击「工时明细」可查看该员工每天的班次与工时，定位超时来源。
      </el-alert>
      <el-table :data="summaryIssues" border stripe size="small" max-height="440">
        <el-table-column type="expand">
          <template #default="{ row }">
            <div style="padding: 8px 16px">
              <el-table :data="employeeHourRows[row.employeeId] || []" size="small" border max-height="260">
                <el-table-column prop="workDate" label="日期" width="110" />
                <el-table-column label="类型" width="60">
                  <template #default="{ row: d }">{{ d.isRestDay === 1 ? '休' : '班' }}</template>
                </el-table-column>
                <el-table-column prop="shiftCode" label="班次" width="80" />
                <el-table-column label="时长(h)" width="90">
                  <template #default="{ row: d }">{{ Number(d.workHours).toFixed(1) }}</template>
                </el-table-column>
              </el-table>
              <div v-if="!(employeeHourRows[row.employeeId] || []).length" style="font-size:12px;color:#909399">（点击右侧「工时明细」加载）</div>
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
import { onMounted, ref, computed } from 'vue'
import { useRoute } from 'vue-router'
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

const summaryIssues = computed(() => issues.value.filter(i => i.workDate == null))

const weekdayNames = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']
function weekdayName(i) { return weekdayNames[i] }

function fmt(t) { return t ? String(t).substring(0, 5) : '--' }

// 返回该员工该日期关联的员工个人问题。
// 岗位缺口(STAFFING_GAP)属于工作站级，在日排班明细的矩阵里按工作站×时段展示；周视图只标记员工个人问题。
function dayIssues(row, date) {
  return issues.value.filter(i =>
    i.issueType === 'CONSECUTIVE_WORK' &&
    i.workDate === date &&
    (i.employeeId === row.employeeId || i.employeeId == null)
  )
}
function dayIssuesClass(row, date) {
  const list = dayIssues(row, date)
  if (!list.length) return ''
  if (list.some(i => i.issueType === 'CONSECUTIVE_WORK')) return 'has-issue-person'
  return ''
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

// 加载某员工整个周期的每日工时（从月视图数据提取）
async function loadEmployeeHours(employeeId) {
  if (employeeHourRows.value[employeeId]) return
  try {
    const month = await getMonthView(planId.value)
    const emp = month.find(e => e.employeeId === employeeId)
    employeeHourRows.value[employeeId] = emp ? emp.days : []
  } catch (e) {
    employeeHourRows.value[employeeId] = []
  }
}

async function loadData() {
  if (!planId.value) {
    errorMsg.value = '请输入排班计划 ID'
    return
  }
  loading.value = true
  errorMsg.value = ''
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

onMounted(loadData)
</script>

<style scoped>
.gantt-wrap { overflow-x: auto; }
.gantt { min-width: 100%; }
.gantt-row { display: flex; border-bottom: 1px solid #ebeef5; }
.gantt-header { background: #f5f7fa; font-weight: 600; }
.gantt-emp-col { width: 150px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; position: sticky; left: 0; background: #fff; z-index: 1; }
.gantt-header .gantt-emp-col { background: #f5f7fa; }
.emp-name { font-size: 13px; }
.emp-sub { font-size: 11px; color: #909399; }
.gantt-day-col { flex: 1; min-width: 120px; padding: 6px; border-right: 1px solid #f5f7fa; }
.gantt-day-col:last-child { border-right: none; }
.day-label { font-size: 13px; text-align: center; }
.day-sub { font-size: 11px; color: #909399; text-align: center; }
.day-block { height: 64px; border-radius: 4px; display: flex; flex-direction: column; align-items: center; justify-content: center; }
.rest-block { background: #fef0f0; color: #f56c6c; font-weight: 600; font-size: 14px; }
.work-block { background: #ecf5ff; color: #409eff; }
.shift-code { font-weight: 600; font-size: 13px; }
.shift-time { font-size: 11px; }
.shift-hours { font-size: 11px; color: #79bbff; }
.empty-block { background: #fafafa; }
.has-issue-person { box-shadow: inset 0 0 0 2px #f56c6c; }
.issue-badge { margin-top: 2px; background: #f56c6c; color: #fff; font-size: 10px; border-radius: 2px; padding: 0 4px; }
.has-gap .issue-badge { background: #e6a23c; }
</style>