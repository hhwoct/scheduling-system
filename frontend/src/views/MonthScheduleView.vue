<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>排班视图</span>
          <div>
            <el-input v-model="planId" placeholder="排班计划 ID" style="width: 200px; margin-right: var(--app-space-4)" />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: var(--app-space-5)" />

      <!-- ========== 日历矩阵 ========== -->
      <div v-loading="loading" class="calendar">
        <div class="cal-header">
          <div v-for="w in ['周一','周二','周三','周四','周五','周六','周日']" :key="w" class="cal-header-cell">{{ w }}</div>
        </div>
        <div v-for="(week, wi) in weeks" :key="wi" class="cal-week">
          <div
            v-for="(day, di) in week"
            :key="day.date || `empty-${wi}-${di}`"
            class="cal-cell"
            :class="{ 'is-empty': !day.date, 'is-selected': day.date === selectedDate }"
            @click="day.date && selectDate(day.date)"
          >
            <template v-if="day.date">
              <div class="cal-day-num">{{ day.dayNum }}</div>
              <div class="cal-work">{{ day.workCount }} 上班</div>
              <div class="cal-rest" v-if="day.restCount > 0">{{ day.restCount }} 休息</div>
              <div class="cal-break" v-if="day.breakCount > 0">{{ day.breakCount }} 人班中休</div>
              <div class="cal-shift" v-if="day.shiftSummary">{{ day.shiftSummary }}</div>
            </template>
          </div>
        </div>
      </div>

      <!-- ========== 日明细 ========== -->
      <div v-if="selectedDate" style="margin-top: var(--app-space-7)">
        <el-divider content-position="left">{{ selectedDate }} 排班明细</el-divider>

        <el-alert v-if="dailyError" :title="dailyError" type="warning" :closable="false" style="margin-bottom: var(--app-space-5)" />

        <div v-loading="dailyLoading" class="matrix-wrap">
          <div class="matrix">
            <!-- 表头 -->
            <div class="m-row m-header">
              <div class="m-ws-col">工作站</div>
              <div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display">
                <span v-if="isHour(slot)">{{ slot.display }}</span>
              </div>
            </div>

            <!-- 每个工作站一行 -->
            <div v-for="ws in dailyWorkstations" :key="ws" class="m-row">
              <div class="m-ws-col">{{ ws }}</div>
              <div v-for="slot in slots" :key="slot.key" class="m-slot-col" :class="cellClass(ws, slot, selectedDate)">
                <div v-if="dailySlotIssues(ws, slot, selectedDate).length" class="gap-flag">缺</div>
                <div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-break': inBreak(emp, slot) }">
                  <div class="emp-name">
                    {{ emp.employeeName }}
                    <span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span>
                  </div>
                  <div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div>
                  <div v-else class="emp-shift break-info">{{ emp.breakCoverEmployeeName ? `顶班 ${emp.breakCoverEmployeeName}` : '' }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { getMonthView, getDailyView, getScheduleIssues } from '../api/schedules'

const route = useRoute()
const planId = ref(route.query.planId || '')
const loading = ref(false)
const rows = ref([])
const weeks = ref([])
const errorMsg = ref('')
const selectedDate = ref('')

// 日明细
const dailyRows = ref([])
const dailyIssues = ref([])
const dailyLoading = ref(false)
const dailyError = ref('')

// 时间轴：13:00 为原点，每 30 分钟一段，共 34 段（13:00~次日 05:30，覆盖 06:00 下班的班次）
const SLOT_COUNT = 34
const slots = computed(() => {
  const list = []
  const startMin = 13 * 60
  for (let i = 0; i < SLOT_COUNT; i++) {
    const totalMin = startMin + i * 30
    const isNextDay = totalMin >= 24 * 60
    const hour = Math.floor((totalMin % (24 * 60)) / 60)
    const minute = totalMin % 60
    const key = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
    list.push({ key, display: isNextDay ? key + '+1' : key, isNextDay })
  }
  return list
})

// 日期 + N 天
function addDays(dateStr, days) {
  if (!dateStr) return ''
  const d = new Date(dateStr + 'T00:00:00')
  d.setDate(d.getDate() + days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 缺口岗位也显示（含当前选中日期及其次日凌晨）
const dailyWorkstations = computed(() => {
  const set = new Set()
  dailyRows.value.forEach(r => {
    if (r.workstationName) set.add(r.workstationName)
  })
  if (selectedDate.value) {
    const nextDay = addDays(selectedDate.value, 1)
    dailyIssues.value.forEach(i => {
      if (i.issueType !== 'STAFFING_GAP' || !i.workstationName) return
      const d = String(i.workDate || '').substring(0, 10)
      if (d === selectedDate.value || d === nextDay) set.add(i.workstationName)
    })
  }
  return Array.from(set)
})

function isHour(slot) { return slot.key.endsWith(':00') }

function dailyCellUsers(ws, slot) {
  return dailyRows.value.filter(r => {
    if (r.workstationName !== ws) return false
    const hm = String(r.timeSlot).substring(0, 5)
    return hm === slot.key
  })
}

// 该员工在该时段是否处于班中休息（含跨午夜回绕）
function inBreak(row, slot) {
  // 兼职员工不显示休息标记（排班界面直接留空）
  if (Number(row.isParttime) === 1) return false
  if (!row.breakStartTime || !row.breakEndTime) return false
  const s = String(row.breakStartTime).substring(0, 5)
  const e = String(row.breakEndTime).substring(0, 5)
  const t = slot.key
  if (e > s) return t >= s && t < e
  return t >= s || t < e
}

function breakTip(row) {
  const s = String(row.breakStartTime).substring(0, 5)
  const e = String(row.breakEndTime).substring(0, 5)
  return row.breakCoverEmployeeName
    ? `休息 ${s}-${e}，由 ${row.breakCoverEmployeeName} 顶班`
    : `休息 ${s}-${e}`
}

function dailySlotIssues(ws, slot, date) {
  const d = slot.isNextDay ? addDays(date, 1) : date
  return dailyIssues.value.filter(i =>
    i.issueType === 'STAFFING_GAP' &&
    String(i.workDate || '').substring(0, 10) === d &&
    i.workstationName === ws &&
    i.timeSlot && String(i.timeSlot).substring(0, 5) === slot.key
  )
}

function cellClass(ws, slot, date) {
  const users = dailyCellUsers(ws, slot)
  const hasGap = dailySlotIssues(ws, slot, date).length > 0
  let cls = ''
  if (users.length > 0) cls += 'has-employee'
  if (slot.isNextDay) cls += ' next-day'
  if (hasGap) cls += ' has-gap'
  return cls
}

async function selectDate(date) {
  selectedDate.value = date
  dailyLoading.value = true
  dailyError.value = ''
  try {
    const [res, iss] = await Promise.all([
      getDailyView(planId.value, date),
      getScheduleIssues(planId.value)
    ])
    dailyRows.value = res?.rows || []
    dailyIssues.value = iss || []
  } catch (e) {
    dailyRows.value = []
    dailyIssues.value = []
    dailyError.value = '加载日明细失败：' + (e?.message || '网络错误')
  } finally {
    dailyLoading.value = false
  }
}

async function loadData() {
  if (!planId.value) {
    errorMsg.value = '请输入排班计划 ID'
    return
  }
  loading.value = true
  errorMsg.value = ''
  selectedDate.value = ''
  try {
    rows.value = await getMonthView(planId.value)
    buildCalendar()
  } catch (e) {
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

function buildCalendar() {
  if (rows.value.length === 0) {
    weeks.value = []
    return
  }

  // P3-10: 从所有行收集日期，避免第一行缺少 days 时遗漏
  const daySet = new Set()
  rows.value.forEach(row => {
    (row.days || []).forEach(d => {
      if (d?.workDate) daySet.add(d.workDate)
    })
  })
  const days = Array.from(daySet).sort()

  if (days.length === 0) {
    weeks.value = []
    return
  }

  const dayStats = {}
  days.forEach(d => { dayStats[d] = { workCount: 0, restCount: 0, breakCount: 0, shifts: new Set() } })

  rows.value.forEach(row => {
    (row.days || []).forEach(d => {
      if (!d || !d.workDate) return
      if (d.isRestDay === 1) {
        // 兼职员工空闲日不算休息（排班界面直接留空）
        if (Number(row.isParttime) !== 1) dayStats[d.workDate].restCount++
      } else {
        dayStats[d.workDate].workCount++
        if (d.breakStartTime && Number(row.isParttime) !== 1) dayStats[d.workDate].breakCount++
        if (d.shiftCode) dayStats[d.workDate].shifts.add(d.shiftCode)
      }
    })
  })

  const firstDate = new Date(days[0] + 'T00:00:00')
  const lastDate = new Date(days[days.length - 1] + 'T00:00:00')
  const firstDayOfWeek = (firstDate.getDay() + 6) % 7
  const calStart = new Date(firstDate)
  calStart.setDate(firstDate.getDate() - firstDayOfWeek)
  const lastDayOfWeek = (lastDate.getDay() + 6) % 7
  const calEnd = new Date(lastDate)
  calEnd.setDate(lastDate.getDate() + (6 - lastDayOfWeek))

  const allWeeks = []
  const cursor = new Date(calStart)
  while (cursor <= calEnd) {
    const week = []
    for (let i = 0; i < 7; i++) {
      const key = formatDate(cursor)
      const inRange = cursor >= firstDate && cursor <= lastDate && dayStats[key]
      if (inRange) {
        const stat = dayStats[key]
        week.push({
          date: key,
          dayNum: cursor.getDate(),
          workCount: stat.workCount,
          restCount: stat.restCount,
          breakCount: stat.breakCount,
          shiftSummary: Array.from(stat.shifts).slice(0, 3).join(' ')
        })
      } else {
        week.push({ date: '', dayNum: '', workCount: 0, restCount: 0, breakCount: 0, shiftSummary: '' })
      }
      cursor.setDate(cursor.getDate() + 1)
    }
    allWeeks.push(week)
  }
  weeks.value = allWeeks
}

function formatDate(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

onMounted(loadData)
</script>

<style scoped>
.calendar { border: 1px solid var(--el-border-color-lighter); border-radius: var(--app-radius-sm); }
.cal-header { display: flex; background: var(--el-fill-color-light); border-bottom: 1px solid var(--el-border-color-lighter); }
.cal-header-cell { flex: 1; text-align: center; padding: var(--app-space-4) 0; font-weight: 600; font-size: var(--app-font-base); }
.cal-week { display: flex; border-bottom: 1px solid var(--el-border-color-lighter); }
.cal-week:last-child { border-bottom: none; }
.cal-cell { flex: 1; min-height: 72px; padding: var(--app-space-3); border-right: 1px solid var(--el-border-color-lighter); cursor: pointer; transition: background-color 0.2s; }
.cal-cell:last-child { border-right: none; }
.cal-cell:hover { background: var(--el-color-primary-light-9); }
.cal-cell.is-empty { background: var(--el-fill-color-lighter); cursor: default; }
.cal-cell.is-selected { background: var(--el-color-primary-light-9); box-shadow: inset 0 0 0 2px var(--el-color-primary); }
.cal-day-num { font-size: var(--app-font-md); font-weight: 600; margin-bottom: var(--app-space-2); }
.cal-work { font-size: var(--app-font-sm); color: var(--el-color-primary); }
.cal-rest { font-size: var(--app-font-sm); color: var(--el-color-danger); }
.cal-break { font-size: var(--app-font-sm); color: var(--el-color-warning); }
.cal-shift { font-size: var(--app-font-xs); color: var(--el-text-color-secondary); margin-top: var(--app-space-2); }

.matrix-wrap { overflow-x: auto; }
.matrix { min-width: 100%; border: 1px solid var(--el-border-color-lighter); border-radius: var(--app-radius-sm); }
.m-row { display: flex; border-bottom: 1px solid var(--el-border-color-lighter); }
.m-row:last-child { border-bottom: none; }
.m-header { background: var(--el-fill-color-light); font-weight: 600; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: var(--app-space-3) var(--app-space-4); border-right: 1px solid var(--el-border-color-lighter); display: flex; align-items: center; }
.m-header .m-ws-col { background: var(--el-fill-color-light); }
.m-slot-col { width: 72px; min-height: 48px; flex-shrink: 0; padding: var(--app-space-1) var(--app-space-2); border-right: 1px solid var(--el-fill-color-light); font-size: var(--app-font-xs); text-align: center; position: relative; }
.m-slot-col:last-child { border-right: none; }
.has-employee { background: var(--el-color-primary-light-9); }
.next-day { background: var(--el-color-warning-light-9); }
.has-gap { box-shadow: inset 0 0 0 2px var(--el-color-danger); }
.gap-flag { position: absolute; top: 1px; right: 1px; background: var(--el-color-danger); color: var(--el-color-white); font-size: var(--app-font-micro); border-radius: var(--app-radius-sm); padding: 0 var(--app-space-2); line-height: 14px; }
.emp-chip { background: var(--el-color-primary); color: var(--el-color-white); border-radius: var(--app-radius-sm); padding: var(--app-space-1) var(--app-space-2); margin-bottom: var(--app-space-1); font-size: var(--app-font-xs); }
.emp-chip.is-break { background: var(--el-color-info); }
.emp-chip.is-break .break-info { color: var(--app-break-text); }
.break-flag { display: inline-block; background: var(--el-color-warning); color: var(--el-color-white); border-radius: var(--app-radius-sm); padding: 0 var(--app-space-2); margin-left: var(--app-space-2); font-size: var(--app-font-micro); line-height: 14px; }
.emp-chip .emp-name { font-weight: 600; }
.emp-chip .emp-shift { opacity: 0.85; font-size: var(--app-font-micro); }
.next-day .emp-chip { background: var(--el-color-warning); }
</style>