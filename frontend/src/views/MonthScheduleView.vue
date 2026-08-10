<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>排班视图</span>
          <div>
            <el-input v-model="planId" placeholder="排班计划 ID" style="width: 200px; margin-right: 8px" />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: 12px" />

      <!-- ========== 日历矩阵 ========== -->
      <div v-loading="loading" class="calendar">
        <div class="cal-header">
          <div v-for="w in ['周一','周二','周三','周四','周五','周六','周日']" :key="w" class="cal-header-cell">{{ w }}</div>
        </div>
        <div v-for="(week, wi) in weeks" :key="wi" class="cal-week">
          <div
            v-for="day in week"
            :key="day.date || '__empty'"
            class="cal-cell"
            :class="{ 'is-empty': !day.date, 'is-selected': day.date === selectedDate }"
            @click="day.date && selectDate(day.date)"
          >
            <template v-if="day.date">
              <div class="cal-day-num">{{ day.dayNum }}</div>
              <div class="cal-work">{{ day.workCount }} 上班</div>
              <div class="cal-rest" v-if="day.restCount > 0">{{ day.restCount }} 休息</div>
              <div class="cal-shift" v-if="day.shiftSummary">{{ day.shiftSummary }}</div>
            </template>
          </div>
        </div>
      </div>

      <!-- ========== 日明细 ========== -->
      <div v-if="selectedDate" style="margin-top: 24px">
        <el-divider content-position="left">{{ selectedDate }} 排班明细</el-divider>

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
                <div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip">
                  <div class="emp-name">{{ emp.employeeName }}</div>
                  <div class="emp-shift">{{ emp.shiftCode || '--' }}</div>
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

const SLOT_COUNT = 28
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

const dailyWorkstations = computed(() => {
  const set = new Set(dailyRows.value.map(r => r.workstationName).filter(Boolean))
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

function dailySlotIssues(ws, slot, date) {
  return dailyIssues.value.filter(i =>
    i.issueType === 'STAFFING_GAP' &&
    i.workDate === date &&
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
  try {
    const [res, iss] = await Promise.all([
      getDailyView(planId.value, date),
      getScheduleIssues(planId.value)
    ])
    dailyRows.value = res || []
    dailyIssues.value = iss || []
  } catch {
    dailyRows.value = []
    dailyIssues.value = []
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

  const days = rows.value[0].days.map(d => d.workDate).sort()
  const dayStats = {}
  days.forEach(d => { dayStats[d] = { workCount: 0, restCount: 0, shifts: new Set() } })

  rows.value.forEach(row => {
    row.days.forEach(d => {
      if (d.isRestDay === 1) dayStats[d.workDate].restCount++
      else {
        dayStats[d.workDate].workCount++
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
          shiftSummary: Array.from(stat.shifts).slice(0, 3).join(' ')
        })
      } else {
        week.push({ date: '', dayNum: '', workCount: 0, restCount: 0, shiftSummary: '' })
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
.calendar { border: 1px solid #ebeef5; border-radius: 4px; }
.cal-header { display: flex; background: #f5f7fa; border-bottom: 1px solid #ebeef5; }
.cal-header-cell { flex: 1; text-align: center; padding: 8px 0; font-weight: 600; font-size: 13px; }
.cal-week { display: flex; border-bottom: 1px solid #ebeef5; }
.cal-week:last-child { border-bottom: none; }
.cal-cell { flex: 1; min-height: 72px; padding: 6px; border-right: 1px solid #ebeef5; cursor: pointer; transition: background-color 0.2s; }
.cal-cell:last-child { border-right: none; }
.cal-cell:hover { background: #ecf5ff; }
.cal-cell.is-empty { background: #fafafa; cursor: default; }
.cal-cell.is-selected { background: #e6f7ff; box-shadow: inset 0 0 0 2px #409eff; }
.cal-day-num { font-size: 14px; font-weight: 600; margin-bottom: 4px; }
.cal-work { font-size: 12px; color: #409eff; }
.cal-rest { font-size: 12px; color: #f56c6c; }
.cal-shift { font-size: 11px; color: #909399; margin-top: 4px; }

.matrix-wrap { overflow-x: auto; }
.matrix { min-width: 100%; border: 1px solid #ebeef5; border-radius: 4px; }
.m-row { display: flex; border-bottom: 1px solid #ebeef5; }
.m-row:last-child { border-bottom: none; }
.m-header { background: #f5f7fa; font-weight: 600; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; display: flex; align-items: center; }
.m-header .m-ws-col { background: #f5f7fa; }
.m-slot-col { width: 72px; min-height: 48px; flex-shrink: 0; padding: 2px 3px; border-right: 1px solid #f5f7fa; font-size: 11px; text-align: center; position: relative; }
.m-slot-col:last-child { border-right: none; }
.has-employee { background: #ecf5ff; }
.next-day { background: #fdf6ec; }
.has-gap { box-shadow: inset 0 0 0 2px #f56c6c; }
.gap-flag { position: absolute; top: 1px; right: 1px; background: #f56c6c; color: #fff; font-size: 10px; border-radius: 2px; padding: 0 3px; line-height: 14px; }
.emp-chip { background: #409eff; color: #fff; border-radius: 3px; padding: 2px 4px; margin-bottom: 2px; font-size: 11px; }
.emp-chip .emp-name { font-weight: 600; }
.emp-chip .emp-shift { opacity: 0.85; font-size: 10px; }
.next-day .emp-chip { background: #e6a23c; }
</style>