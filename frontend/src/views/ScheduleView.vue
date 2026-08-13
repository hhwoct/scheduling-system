<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>排班查看</span>
          <el-button type="primary" :loading="loading" @click="loadAll" :disabled="!planId">查询</el-button>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" closable @close="errorMsg=''" />

      <el-table :data="plans" v-loading="plansLoading" border stripe size="small" style="margin-bottom: 16px" highlight-current-row @current-change="selectPlan">
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="planName" label="计划名称" />
        <el-table-column label="周期" width="200">
          <template #default="{ row }">{{ row.startDate }} ~ {{ row.endDate }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.status === 'PUBLISHED' ? 'success' : 'info'">{{ row.status === 'PUBLISHED' ? '已发布' : '草稿' }}</el-tag>
          </template>
        </el-table-column>
      </el-table>

      <el-radio-group v-if="planId" v-model="viewMode" @change="onModeChange" style="margin-bottom: 16px">
        <el-radio-button label="week">周视图</el-radio-button>
        <el-radio-button label="month">月视图</el-radio-button>
        <el-radio-button label="day">日明细</el-radio-button>
        <el-radio-button label="issues">问题详情</el-radio-button>
      </el-radio-group>

      <div v-if="viewMode === 'week'" v-loading="loading">
        <el-date-picker v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择起始日" @change="loadWeek" />
        <div class="gantt">
          <div class="gantt-row gantt-header">
            <div class="gantt-emp-col">员工</div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <div class="day-label">{{ weekdayName(d.weekday) }}</div><div class="day-sub">{{ d.date }}</div>
            </div>
          </div>
          <div v-for="row in weekRows" :key="row.employeeId" class="gantt-row">
            <div class="gantt-emp-col"><div class="emp-name">{{ row.employeeName }}</div><div class="emp-sub">{{ row.department }}</div></div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <template v-if="getWeekDay(row, d.date)">
                <div v-if="getWeekDay(row, d.date).isRestDay === 1" class="day-block rest-block">休</div>
                <div v-else class="day-block work-block">
                  <div class="shift-code">{{ getWeekDay(row, d.date).shiftCode || '班' }}</div>
                  <div class="shift-time">{{ fmt(getWeekDay(row, d.date).startTime) }}-{{ fmt(getWeekDay(row, d.date).endTime) }}</div>
                </div>
              </template>
              <div v-else class="day-block empty-block"></div>
            </div>
          </div>
        </div>

        <!-- 兼职替补需求色块：低技能岗位缺口 -->
        <div v-if="weekPartTimeNeeds.length" class="parttime-block" style="margin-top: 16px">
          <div class="parttime-title">
            <span class="parttime-badge">兼</span>
            兼职替补需求（低技能岗位缺口，建议寻找兼职人员临时填补）
          </div>
          <div class="parttime-table">
            <div class="parttime-row parttime-header">
              <div class="parttime-ws-col">岗位</div>
              <div v-for="d in weekDays" :key="d.date" class="parttime-day-col">{{ d.date.substring(5) }}</div>
            </div>
            <div v-for="need in weekPartTimeNeeds" :key="need.workstationName + (need.days || '')" class="parttime-row">
              <div class="parttime-ws-col">{{ need.workstationName }}</div>
              <div
                v-for="d in weekDays"
                :key="d.date"
                class="parttime-day-col"
                :class="{ active: hasPartTimeNeed(need.workstationName, d.date) }"
                :title="hasPartTimeNeed(need.workstationName, d.date) ? `${need.workstationName} ${d.date} 缺口，建议找兼职替补` : ''"
              ></div>
            </div>
          </div>
          <div class="parttime-legend">
            <span class="legend-box parttime-legend-box"></span> 该日该低技能岗位存在缺口 → 建议寻找兼职人员临时替补
          </div>
        </div>
      </div>

      <div v-if="viewMode === 'month'" v-loading="loading">
        <div class="calendar">
          <div class="cal-header">
            <div v-for="w in ['周一','周二','周三','周四','周五','周六','周日']" :key="w" class="cal-header-cell">{{ w }}</div>
          </div>
          <div v-for="(week, wi) in weeks" :key="wi" class="cal-week">
            <div v-for="(day, di) in week" :key="day.date || `empty-${wi}-${di}`" class="cal-cell" :class="{ 'is-empty': !day.date, 'is-selected': day.date === selectedDate }" @click="day.date && selectDate(day.date)">
              <template v-if="day.date">
                <div class="cal-day-num">{{ day.dayNum }}</div>
                <div class="cal-work">{{ day.workCount }} 上班</div>
                <div class="cal-rest" v-if="day.restCount > 0">{{ day.restCount }} 休息</div>
                <div class="cal-shift" v-if="day.shiftSummary">{{ day.shiftSummary }}</div>
              </template>
            </div>
          </div>
        </div>
        <div v-if="selectedDate" style="margin-top: 24px">
          <el-divider content-position="left">{{ selectedDate }}</el-divider>
          <div v-loading="dailyLoading" class="matrix-wrap"><div class="matrix">
            <div class="m-row m-header"><div class="m-ws-col">工作站</div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display"><span v-if="isHour(slot)">{{ slot.display }}</span></div></div>
            <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :class="cellClass(ws, slot)"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip"><div class="emp-name">{{ emp.employeeName }}</div><div class="emp-shift">{{ emp.shiftCode || '--' }}</div></div></div></div>
          </div></div>
        </div>
      </div>

      <div v-if="viewMode === 'day'" v-loading="loading">
        <el-date-picker v-model="dayDate" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择日期" @change="loadDay" />
        <div v-if="dayDate" class="matrix-wrap"><div class="matrix">
          <div class="m-row m-header"><div class="m-ws-col">工作站</div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display"><span v-if="isHour(slot)">{{ slot.display }}</span></div></div>
          <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :class="cellClass(ws, slot)"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip"><div class="emp-name">{{ emp.employeeName }}</div><div class="emp-shift">{{ emp.shiftCode || '--' }}</div></div></div></div>
        </div></div>
      </div>

      <div v-if="viewMode === 'issues'" v-loading="issuesLoading">
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num">{{ issuesList.length }}</div><div class="stat-label">问题总数</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#f56c6c">{{ issueStats.gapCount }}</div><div class="stat-label">岗位缺口</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#e6a23c">{{ issueStats.warnCount }}</div><div class="stat-label">警告级别</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#67c23a">{{ issueStats.daysCount }}</div><div class="stat-label">影响天数</div></el-card></el-col>
        </el-row>
        <el-row :gutter="16" style="margin-bottom: 16px" align="stretch">
          <el-col :span="9">
            <el-card header="问题分析" style="height: 100%">
              <div style="display: flex; align-items: flex-start; gap: 12px; flex-wrap: wrap">
                <div style="display: flex; align-items: center; gap: 8px">
                  <svg viewBox="0 0 200 200" width="220" height="220">
                    <circle v-for="(slice, i) in typePieData" :key="i" :cx="100" :cy="100" :r="80" fill="none" :stroke="slice.color" stroke-width="30" :stroke-dasharray="`${slice.pct * 502.65} ${(1 - slice.pct) * 502.65}`" :stroke-dashoffset="(typePieOffset[i])" transform="rotate(-90 100 100)" style="cursor: pointer" @click="filterTableByType(slice.key)" />
                    <text v-for="(slice, i) in typePieLabels" :key="'tlbl'+i" :x="slice.x" :y="slice.y" text-anchor="middle" font-size="11" fill="#fff" font-weight="bold" pointer-events="none">{{ slice.count }}</text>
                    <text x="100" y="95" text-anchor="middle" font-size="15" fill="#303133" font-weight="bold">{{ issuesList.length }} 条</text>
                    <text x="100" y="114" text-anchor="middle" font-size="11" fill="#909399">类型分布</text>
                    <text v-if="typeFilter" x="100" y="128" text-anchor="middle" font-size="9" fill="#409eff" style="cursor:pointer" @click="typeFilter=''">✕ 清除</text>
                  </svg>
                  <div class="mini-legend"><div v-for="s in typePieData" :key="s.label" class="legend-row clickable" @click="filterTableByType(s.key)"><span class="legend-dot" :style="{ background: s.color }"></span><span style="font-size:12px" :style="{ fontWeight: typeFilter === s.key ? 'bold' : 'normal', color: typeFilter === s.key ? '#409eff' : '#606266' }">{{ s.label }} ({{ s.count }})</span></div></div>
                </div>
                <div style="display: flex; align-items: center; gap: 8px">
                  <svg viewBox="0 0 200 200" width="220" height="220">
                    <circle v-for="(slice, i) in wsPieData" :key="i" :cx="100" :cy="100" :r="80" fill="none" :stroke="slice.color" stroke-width="30" :stroke-dasharray="`${slice.pct * 502.65} ${(1 - slice.pct) * 502.65}`" :stroke-dashoffset="(wsPieOffset[i])" transform="rotate(-90 100 100)" style="cursor: pointer" @click="filterTableByWs(slice.name)" />
                    <text x="100" y="95" text-anchor="middle" font-size="15" fill="#303133" font-weight="bold">{{ wsTotal }} 条</text>
                    <text x="100" y="114" text-anchor="middle" font-size="11" fill="#909399">缺口分布</text>
                    <text v-if="wsFilter" x="100" y="128" text-anchor="middle" font-size="9" fill="#409eff" style="cursor:pointer" @click="wsFilter=''">✕ 清除</text>
                  </svg>
                  <div class="mini-legend"><div v-for="s in wsPieData" :key="s.name" class="legend-row clickable" @click="filterTableByWs(s.name)"><span class="legend-dot" :style="{ background: s.color }"></span><span style="font-size:12px" :style="{ fontWeight: wsFilter === s.name ? 'bold' : 'normal', color: wsFilter === s.name ? '#409eff' : '#606266' }">{{ s.name }} ({{ s.count }})</span></div></div>
                </div>
              </div>
            </el-card>
          </el-col>
          <el-col :span="15">
            <el-card header="排班合理度趋势" style="height: 100%">
              <div v-if="rationalityData.length" class="area-chart-wrap" style="padding: 2px 6px 6px">
                <svg :viewBox="`0 0 ${rationalityData.length * 80 + 40} 300`" width="100%" height="290" preserveAspectRatio="xMidYMid meet">
                  <!-- Y轴网格线 -->
                  <line v-for="tick in yTicks" :key="'grid'+tick" :x1="40" :y1="Y(tick)" :x2="rationalityData.length * 80 + 30" :y2="Y(tick)" stroke="#ebeef5" stroke-width="1" />
                  <!-- Y轴标签 -->
                  <text v-for="tick in yTicks" :key="'ylbl'+tick" x="36" :y="Y(tick) + 4" text-anchor="end" font-size="13" font-weight="bold" fill="#606266">{{ tick }}%</text>
                  <!-- X轴标签 -->
                  <text v-for="(d, i) in rationalityData" :key="'xlbl'+i" :x="40 + i * 80 + 25" y="285" text-anchor="middle" font-size="13" font-weight="bold" fill="#303133">{{ d.date.substring(5) }}</text>
                  <!-- 填色区域 -->
                  <path :d="areaPath" fill="rgba(64,158,255,0.15)" />
                  <!-- 曲线 -->
                  <path :d="linePath" fill="none" stroke="#409eff" stroke-width="2.5" />
                  <!-- 数据点 -->
                  <circle v-for="(d, i) in rationalityData" :key="'dot'+i" :cx="40 + i * 80 + 25" :cy="Y(d.pct)" r="4" fill="#fff" stroke="#409eff" stroke-width="2" style="cursor: pointer" @click="filterTableByDate(d.date)" />
                  <text v-for="(d, i) in rationalityData" :key="'v'+i" :x="40 + i * 80 + 25" :y="Y(d.pct) - 10" text-anchor="middle" font-size="13" fill="#303133" font-weight="bold">{{ Math.round(d.pct) }}%</text>
                  <text v-if="dateFilter" x="50%" y="14" text-anchor="middle" font-size="10" fill="#409eff" style="cursor:pointer" @click="dateFilter=''">✕ 清除日期筛选</text>
                </svg>
              </div>
            </el-card>
          </el-col>
        </el-row>
        <el-table :data="filteredIssues" border stripe size="small" max-height="400">
          <el-table-column prop="workDate" label="日期" width="110" />
          <el-table-column label="时段" width="90"><template #default="{ row }">{{ row.timeSlot ? String(row.timeSlot).substring(0, 5) : '整周期' }}</template></el-table-column>
          <el-table-column prop="workstationName" label="工作站" width="120"><template #default="{ row }">{{ row.workstationName || '--' }}</template></el-table-column>
          <el-table-column label="类型" width="110"><template #default="{ row }"><el-tag v-if="row.issueType === 'STAFFING_GAP'" type="danger" size="small">岗位缺口</el-tag><el-tag v-else-if="row.issueType === 'SKILL_MISMATCH'" type="warning" size="small">技能不匹配</el-tag><el-tag v-else-if="row.issueType === 'OVERTIME'" type="info" size="small">工时超限</el-tag><el-tag v-else-if="row.issueType === 'CONSECUTIVE_WORK'" type="info" size="small">连续工作超限</el-tag><el-tag v-else size="small">{{ row.issueType }}</el-tag></template></el-table-column>
          <el-table-column prop="severity" label="严重度" width="80"><template #default="{ row }"><el-tag :type="row.severity === 'ERROR' ? 'danger' : 'warning'" size="small">{{ row.severity === 'ERROR' ? '错误' : '警告' }}</el-tag></template></el-table-column>
          <el-table-column prop="description" label="说明" min-width="280" show-overflow-tooltip />
        </el-table>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { getMonthView, getWeekView, getDailyView, getScheduleIssues, getScheduleRationality, getSchedules } from '../api/schedules'

const route = useRoute()
const planId = ref(route.query.planId || '')
const loading = ref(false)
const errorMsg = ref('')
const viewMode = ref(route.query.mode || 'week')
const plans = ref([])
const plansLoading = ref(false)
const weekRows = ref([])
const weekDays = ref([])
const weekStart = ref('')
const monthRows = ref([])
const weeks = ref([])
const selectedDate = ref('')
const dailyRows = ref([])
const dailyIssues = ref([])
const dailyLoading = ref(false)
const dayDate = ref('')
const issuesList = ref([])
const issuesLoading = ref(false)
const rationalityList = ref([])
// 周视图：低技能岗位兼职替补需求（岗位×日期 → 是否有缺口）
const weekPartTimeNeeds = ref([])
const weekPartTimeMap = ref({})

function weekdayName(i) { return ['周一','周二','周三','周四','周五','周六','周日'][i] }
function fmt(t) { return t ? String(t).substring(0, 5) : '--' }
function getWeekDay(row, date) { return row.days?.find(d => d.workDate === date) }

const SLOT_COUNT = 28
const slots = computed(() => Array.from({ length: SLOT_COUNT }, (_, i) => {
  const min = 13 * 60 + i * 30
  const isNext = min >= 24 * 60
  const h = Math.floor((min % (24 * 60)) / 60)
  const m = min % 60
  const key = `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`
  return { key, display: isNext ? key + '+1' : key, isNextDay: isNext }
}))
// P3-11: 缺口岗位也显示
const dailyWorkstations = computed(() => {
  const set = new Set()
  dailyRows.value.forEach(r => {
    if (r.workstationName) set.add(r.workstationName)
  })
  dailyIssues.value
    .filter(i => i.issueType === 'STAFFING_GAP' && i.workDate === (selectedDate.value || dayDate.value))
    .forEach(i => {
      if (i.workstationName) set.add(i.workstationName)
    })
  return Array.from(set)
})
function isHour(s) { return s.key.endsWith(':00') }
function dailyCellUsers(ws, slot) { return dailyRows.value.filter(r => r.workstationName === ws && String(r.timeSlot).substring(0,5) === slot.key) }
// P3-12: 缺口按日期过滤
function dailySlotIssues(ws, slot) {
  const currentDate = selectedDate.value || dayDate.value
  return dailyIssues.value.filter(i => i.issueType === 'STAFFING_GAP' && i.workDate === currentDate && i.workstationName === ws && i.timeSlot && String(i.timeSlot).substring(0,5) === slot.key)
}
// 低技能岗位缺口：绿色标记 + 兼职建议
function lowSkillGapIssues(ws, slot) {
  return dailySlotIssues(ws, slot).filter(i => i.isLowSkill)
}
// 判断工作站是否低技能岗位
function wsLowSkill(ws) {
  return dailyIssues.value.some(i => i.workstationName === ws && i.isLowSkill)
}
// 缺口提示：低技能岗位显示兼职建议
function gapTooltip(ws, slot) {
  const low = lowSkillGapIssues(ws, slot)
  if (low.length > 0) {
    return '该岗位技术含量低，建议寻找兼职人员临时填补'
  }
  return '岗位缺口'
}
function cellClass(ws, slot) {
  return [
    dailyCellUsers(ws,slot).length > 0 && 'has-employee',
    slot.isNextDay && 'next-day',
    dailySlotIssues(ws,slot).length > 0 && (lowSkillGapIssues(ws, slot).length > 0 ? 'has-gap-low-skill' : 'has-gap')
  ].filter(Boolean).join(' ')
}

async function loadAll() {
  loading.value = true; errorMsg.value = ''
  try {
    if (viewMode.value === 'issues') await loadIssues()
    else if (viewMode.value === 'week') await loadWeek()
    else if (viewMode.value === 'month') await loadMonth()
    else if (viewMode.value === 'day') await loadDay()
  } catch (e) { errorMsg.value = e.message }
  finally { loading.value = false }
}

// 回退：从缺口描述计算每日合理度（缺N人/需求M）
function computeRationalityFromIssues(issues) {
  const demandByDate = {}
  const missingByDate = {}
  ;(issues || []).filter(i => i.issueType === 'STAFFING_GAP' && i.workDate).forEach(i => {
    const d = i.workDate
    const desc = i.description || ''
    const mDemand = desc.match(/需求\s*(\d+)/)
    const mMissing = desc.match(/缺\s*(\d+)\s*人/)
    const demand = mDemand ? parseInt(mDemand[1]) : 0
    const missing = mMissing ? parseInt(mMissing[1]) : 0
    demandByDate[d] = (demandByDate[d] || 0) + demand
    missingByDate[d] = (missingByDate[d] || 0) + missing
  })
  return Object.keys(demandByDate).sort().map(d => {
    const demand = demandByDate[d] || 1
    const missing = missingByDate[d] || 0
    return { date: d, pct: Math.max(0, Math.min(100, Math.round((1 - missing / demand) * 100))) }
  })
}

async function loadIssues() {
  if (!planId.value) return
  issuesLoading.value = true
  try {
    const issues = (await getScheduleIssues(planId.value)) || []
    issuesList.value = issues
    // 优先使用后端 /rationality 端点；404/失败时回退从缺口描述解析（兼容未升级的后端）
    try {
      const rationality = await getScheduleRationality(planId.value)
      rationalityList.value = rationality || []
    } catch (e) {
      rationalityList.value = computeRationalityFromIssues(issues)
    }
  } finally { issuesLoading.value = false }
}

function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

async function loadWeek() {
  if (!planId.value) { errorMsg.value = '请输入计划ID'; return }
  if (!weekStart.value) { const ps = await getSchedules({ page: 1, pageSize: 1 }); const p = ps.items?.find(x => x.id == planId.value); weekStart.value = p?.startDate || getToday() }
  weekRows.value = await getWeekView(planId.value, weekStart.value)
  const firstRow = weekRows.value[0]
  weekDays.value = firstRow?.days?.map(d => ({ date: d.workDate, weekday: new Date(d.workDate + 'T00:00:00').getDay() === 0 ? 6 : new Date(d.workDate + 'T00:00:00').getDay() - 1 })) || []

  // 加载低技能岗位缺口 → 生成兼职替补需求色块
  try {
    const iss = await getScheduleIssues(planId.value)
    const lowSkillGaps = (iss || []).filter(i => i.issueType === 'STAFFING_GAP' && i.isLowSkill && i.workDate && i.workstationName)
    const map = {}
    const wsSet = new Set()
    lowSkillGaps.forEach(i => {
      map[`${i.workstationName}|${i.workDate}`] = true
      wsSet.add(i.workstationName)
    })
    weekPartTimeMap.value = map
    weekPartTimeNeeds.value = Array.from(wsSet).map(ws => ({ workstationName: ws }))
  } catch (e) {
    weekPartTimeNeeds.value = []
    weekPartTimeMap.value = {}
  }
}

// 该低技能岗位在该日是否有兼职缺口
function hasPartTimeNeed(ws, date) {
  return !!weekPartTimeMap.value[`${ws}|${date}`]
}

async function loadMonth() { if (!planId.value) { errorMsg.value = '请输入计划ID'; return }; monthRows.value = await getMonthView(planId.value); buildCalendar() }

async function loadDay(date) { const d = date || dayDate.value; if (!planId.value || !d) return; dailyLoading.value = true; try { const [res, iss] = await Promise.all([getDailyView(planId.value, d), getScheduleIssues(planId.value)]); dailyRows.value = res || []; dailyIssues.value = iss || [] } finally { dailyLoading.value = false } }

async function selectDate(date) { selectedDate.value = date; await loadDay(date) }

function onModeChange() { selectedDate.value = ''; dailyRows.value = []; loadAll() }

function buildCalendar() {
  if (!monthRows.value.length) { weeks.value = []; return }
  // P3-10: 从所有行收集日期
  const daySet = new Set()
  monthRows.value.forEach(row => {
    (row.days || []).forEach(d => {
      if (d?.workDate) daySet.add(d.workDate)
    })
  })
  const days = Array.from(daySet).sort()
  if (days.length === 0) { weeks.value = []; return }
  const stats = {}; days.forEach(d => { stats[d] = { w: 0, r: 0, s: new Set() } })
  monthRows.value.forEach(row => row.days.forEach(d => { if (d.isRestDay === 1) stats[d.workDate].r++; else { stats[d.workDate].w++; if (d.shiftCode) stats[d.workDate].s.add(d.shiftCode) } }))
  const fd = new Date(days[0] + 'T00:00:00'), ld = new Date(days[days.length - 1] + 'T00:00:00')
  const cs = new Date(fd); cs.setDate(fd.getDate() - ((fd.getDay() + 6) % 7))
  const ce = new Date(ld); ce.setDate(ld.getDate() + (6 - (ld.getDay() + 6) % 7))
  const ws = []; const c = new Date(cs)
  while (c <= ce) {
    const w = []
    for (let i = 0; i < 7; i++) {
      const k = `${c.getFullYear()}-${String(c.getMonth()+1).padStart(2,'0')}-${String(c.getDate()).padStart(2,'0')}`
      if (c >= fd && c <= ld && stats[k]) { const s = stats[k]; w.push({ date: k, dayNum: c.getDate(), workCount: s.w, restCount: s.r, shiftSummary: Array.from(s.s).slice(0,3).join(' ') }) }
      else w.push({ date: '', dayNum: '', workCount: 0, restCount: 0, shiftSummary: '' })
      c.setDate(c.getDate() + 1)
    }
    ws.push(w)
  }
  weeks.value = ws
}

// P3-39: 切换计划时重置所有状态
function selectPlan(row) {
  if (!row) return
  planId.value = row.id
  weekStart.value = row.startDate || ''
  selectedDate.value = ''
  dayDate.value = ''
  dailyRows.value = []
  dailyIssues.value = []
  monthRows.value = []
  issuesList.value = []
  viewMode.value = 'week'
  loadAll()
}

async function loadPlans() { plansLoading.value = true; try { const res = await getSchedules({ page: 1, pageSize: 100 }); plans.value = res.items || [] } finally { plansLoading.value = false } }

const issueStats = computed(() => {
  const list = issuesList.value
  return { gapCount: list.filter(i => i.issueType === 'STAFFING_GAP').length, warnCount: list.filter(i => i.severity === 'WARN').length, daysCount: new Set(list.map(i => i.workDate).filter(Boolean)).size }
})

const COLORS_ARR = ['#f56c6c','#e6a23c','#409eff','#67c23a','#909399']
const typePieData = computed(() => {
  const map = {}; issuesList.value.forEach(i => { map[i.issueType] = (map[i.issueType] || 0) + 1 })
  const entries = Object.entries(map); const total = entries.reduce((s, [,c]) => s + c, 0) || 1
  return entries.map(([k, c], idx) => ({ key: k, label: k === 'STAFFING_GAP' ? '岗位缺口' : k === 'SKILL_MISMATCH' ? '技能不匹配' : k === 'OVERTIME' ? '工时超限' : k === 'CONSECUTIVE_WORK' ? '连续工作超限' : k, count: c, pct: c / total, color: COLORS_ARR[idx % COLORS_ARR.length] }))
})
const typePieOffset = computed(() => { let sum = 0; return typePieData.value.map(s => { const v = -sum * 502.65; sum += s.pct; return v }) })
const typePieLabels = computed(() => { const r = 63; let cum = -Math.PI / 2; return typePieData.value.map(s => { const half = s.pct * Math.PI; const mid = cum + half; const x = 100 + r * Math.cos(mid); const y = 100 + r * Math.sin(mid); cum += s.pct * 2 * Math.PI; return { x: Math.round(x), y: Math.round(y), count: s.count } }) })

// P3-32: 用完整总数计算比例，再取前8，并补「其他」段使环形闭合
const wsPieData = computed(() => {
  const map = {}; issuesList.value.filter(i => i.issueType === 'STAFFING_GAP').forEach(i => { const ws = i.workstationName || '未知'; map[ws] = (map[ws] || 0) + 1 })
  const allEntries = Object.entries(map).sort((a,b) => b[1]-a[1])
  const total = allEntries.reduce((s,[,c]) => s+c, 0) || 1
  const top = allEntries.slice(0, 8)
  const topSum = top.reduce((s,[,c]) => s + c, 0)
  const result = top.map(([name, count], idx) => ({ name, count, pct: count / total, color: COLORS_ARR[idx % COLORS_ARR.length] }))
  // 补「其他」段：前8之外的计数归入一段，保证比例总和=100%，环形闭合
  const restCount = total - topSum
  if (allEntries.length > 8 && restCount > 0) {
    result.push({ name: '其他', count: restCount, pct: restCount / total, color: '#c0c4cc' })
  }
  return result
})
const wsPieOffset = computed(() => { let sum = 0; return wsPieData.value.map(s => { const v = -sum * 502.65; sum += s.pct; return v }) })
const wsTotal = computed(() => wsPieData.value.reduce((s, d) => s + d.count, 0))
const wsPieLabels = computed(() => { const r = 63; let cum = -Math.PI / 2; return wsPieData.value.map(s => { const half = s.pct * Math.PI; const mid = cum + half; const x = 100 + r * Math.cos(mid); const y = 100 + r * Math.sin(mid); cum += s.pct * 2 * Math.PI; return { x: Math.round(x), y: Math.round(y), count: s.count } }) })

const wsFilter = ref('')
const typeFilter = ref('')
const dateFilter = ref('')
let isFilterActive = computed(() => wsFilter.value || typeFilter.value || dateFilter.value)

function filterTableByWs(wsName) { wsFilter.value = wsFilter.value === wsName ? '' : wsName }
function filterTableByType(type) { typeFilter.value = typeFilter.value === type ? '' : type }
function filterTableByDate(date) { dateFilter.value = dateFilter.value === date ? '' : date }

const filteredIssues = computed(() => {
  let list = issuesList.value
  if (wsFilter.value) list = list.filter(i => (i.workstationName || '未知') === wsFilter.value)
  if (typeFilter.value) list = list.filter(i => i.issueType === typeFilter.value)
  if (dateFilter.value) list = list.filter(i => i.workDate === dateFilter.value)
  return list
})
const activeFilterCount = computed(() => {
  let count = 0
  if (wsFilter.value) count++
  if (typeFilter.value) count++
  if (dateFilter.value) count++
  return count
})

const dailyBars = computed(() => {
  const map = {}
  issuesList.value.filter(i => i.issueType === 'STAFFING_GAP').forEach(i => {
    const d = i.workDate || '未知'
    map[d] = (map[d] || 0) + 1
  })
  const entries = Object.entries(map).sort((a,b) => a[0].localeCompare(b[0]))
  const max = Math.max(...entries.map(([,c]) => c), 1)
  return entries.map(([date, count], idx) => ({ date, count, pct: Math.round(count / max * 100), color: COLORS_ARR[idx % COLORS_ARR.length] }))
})

// 合理度 = 已满足需求 ÷ 总需求 × 100（量纲统一为“人”）
// 合理度：使用后端返回的每日 Rationality（实际排班覆盖 ÷ 需求）
const rationalityData = computed(() => {
  if (rationalityList.value && rationalityList.value.length) {
    return rationalityList.value.map(r => ({ date: r.date, pct: r.pct }))
  }
  return []
})
const yTicks = [0, 25, 50, 75, 100]
function Y(pct) { return 270 - (pct / 125) * 250 }
const areaPath = computed(() => {
  const pts = rationalityData.value
  if (pts.length === 0) return ''
  let d = `M 65 ${Y(pts[0].pct)} `
  for (let i = 1; i < pts.length; i++) {
    const x0 = 40 + (i - 1) * 80 + 25
    const y0 = Y(pts[i-1].pct)
    const x1 = 40 + i * 80 + 25
    const y1 = Y(pts[i].pct)
    const cx1 = x0 + 25
    const cx2 = x1 - 25
    d += `C ${cx1} ${y0} ${cx2} ${y1} ${x1} ${y1} `
  }
  d += `L ${40 + pts.length * 80 + 15} 270 L 65 270 Z`
  return d
})
const linePath = computed(() => {
  const pts = rationalityData.value
  if (pts.length === 0) return ''
  let d = `M 65 ${Y(pts[0].pct)} `
  for (let i = 1; i < pts.length; i++) {
    const x0 = 40 + (i - 1) * 80 + 25
    const y0 = Y(pts[i-1].pct)
    const x1 = 40 + i * 80 + 25
    const y1 = Y(pts[i].pct)
    const cx1 = x0 + 25
    const cx2 = x1 - 25
    d += `C ${cx1} ${y0} ${cx2} ${y1} ${x1} ${y1} `
  }
  return d
})

onMounted(() => { loadPlans(); if (planId.value) loadAll() })
</script>

<style scoped>
.gantt { border: 1px solid #ebeef5; border-radius: 4px; }
.gantt-row { display: flex; border-bottom: 1px solid #ebeef5; }
.gantt-header { background: #f5f7fa; font-weight: 600; }
.gantt-emp-col { width: 140px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; }
.gantt-day-col { flex: 1; min-width: 100px; padding: 4px; border-right: 1px solid #ebeef5; }
.day-label { font-size: 12px; color: #606266; }
.day-sub { font-size: 11px; color: #909399; }
.day-block { border-radius: 4px; padding: 6px; text-align: center; font-size: 12px; min-height: 40px; }
.work-block { background: #ecf5ff; }
.rest-block { background: #fef0f0; color: #f56c6c; font-weight: 600; }
.empty-block { background: #fafafa; }
.shift-code { font-weight: 600; }
.shift-time { font-size: 11px; color: #909399; }
.emp-name { font-size: 12px; font-weight: 600; }
.emp-sub { font-size: 11px; color: #909399; }
.calendar { border: 1px solid #ebeef5; border-radius: 4px; }
.cal-header { display: flex; background: #f5f7fa; }
.cal-header-cell { flex: 1; text-align: center; padding: 4px 6px 0; font-weight: 600; font-size: 13px; }
.cal-week { display: flex; border-bottom: 1px solid #ebeef5; }
.cal-week:last-child { border-bottom: none; }
.cal-cell { flex: 1; min-height: 72px; padding: 6px; border-right: 1px solid #ebeef5; cursor: pointer; }
.cal-cell:hover { background: #ecf5ff; }
.cal-cell.is-empty { background: #fafafa; cursor: default; }
.cal-cell.is-selected { background: #e6f7ff; box-shadow: inset 0 0 0 2px #409eff; }
.cal-day-num { font-size: 14px; font-weight: 600; margin-bottom: 4px; }
.cal-work { font-size: 12px; color: #409eff; }
.cal-rest { font-size: 12px; color: #f56c6c; }
.cal-shift { font-size: 11px; color: #909399; margin-top: 4px; }
.matrix-wrap { overflow-x: auto; }
.matrix { border: 1px solid #ebeef5; border-radius: 4px; }
.m-row { display: flex; border-bottom: 1px solid #ebeef5; }
.m-row:last-child { border-bottom: none; }
.m-header { background: #f5f7fa; font-weight: 600; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; display: flex; align-items: center; }
.m-slot-col { width: 72px; min-height: 48px; flex-shrink: 0; padding: 2px 3px; border-right: 1px solid #f5f7fa; font-size: 11px; text-align: center; position: relative; }
.m-slot-col:last-child { border-right: none; }
.has-employee { background: #ecf5ff; }
.next-day { background: #fdf6ec; }
.has-gap { box-shadow: inset 0 0 0 2px #f56c6c; }
.has-gap-low-skill { box-shadow: inset 0 0 0 2px #67c23a; background: #f0f9eb; }
.gap-flag { position: absolute; top: 1px; right: 1px; background: #f56c6c; color: #fff; font-size: 10px; border-radius: 2px; padding: 0 3px; line-height: 14px; }
.gap-flag-low { background: #67c23a; }
.emp-chip { background: #409eff; color: #fff; border-radius: 3px; padding: 2px 4px; margin-bottom: 2px; font-size: 11px; }
.emp-chip .emp-name { font-weight: 600; }
.emp-chip .emp-shift { opacity: 0.85; font-size: 10px; }
.next-day .emp-chip { background: #e6a23c; }
.stat-num { font-size: 28px; font-weight: 700; color: #303133; }
.stat-label { font-size: 13px; color: #909399; margin-top: 4px; }
.pie-wrap { display: flex; align-items: center; gap: 16px; }
.pie-legend { display: flex; flex-direction: column; gap: 6px; }
.legend-row { display: flex; align-items: center; gap: 6px; font-size: 12px; }
.legend-row.clickable { cursor: pointer; user-select: none; }
.legend-row.clickable:hover { background: #f5f7fa; border-radius: 4px; }
.legend-dot { width: 12px; height: 12px; border-radius: 50%; display: inline-block; }
.bar-chart { display: flex; flex-direction: column; gap: 3px; max-height: 260px; overflow-y: auto; }
.bar-row { display: flex; align-items: center; gap: 6px; }
.bar-label { width: 42px; font-size: 10px; text-align: right; color: #606266; flex-shrink: 0; }
.bar-track { flex: 1; height: 14px; background: #f5f7fa; border-radius: 7px; overflow: hidden; }
.bar-fill { height: 100%; border-radius: 7px; transition: width 0.3s; }
.bar-val { width: 24px; font-size: 11px; color: #303133; font-weight: 600; flex-shrink: 0; }
.mini-pie { display: flex; flex-direction: column; align-items: center; }
.mini-legend { margin-top: 4px; }
.parttime-block { border: 1px solid #67c23a; border-radius: 6px; padding: 12px; background: #f0f9eb; }
.parttime-title { display: flex; align-items: center; gap: 6px; font-weight: 600; color: #303133; margin-bottom: 10px; }
.parttime-badge { display: inline-block; background: #67c23a; color: #fff; border-radius: 3px; font-size: 12px; padding: 0 6px; line-height: 18px; }
.parttime-table { border: 1px solid #c2e7b0; border-radius: 4px; overflow: hidden; }
.parttime-row { display: flex; border-bottom: 1px solid #e8f5e0; }
.parttime-row:last-child { border-bottom: none; }
.parttime-header { background: #f0f9eb; font-weight: 600; }
.parttime-ws-col { width: 90px; flex-shrink: 0; padding: 5px 8px; border-right: 1px solid #e8f5e0; font-size: 12px; }
.parttime-day-col { flex: 1; min-height: 20px; padding: 3px; border-right: 1px solid #e8f5e0; font-size: 11px; text-align: center; color: #909399; }
.parttime-day-col:last-child { border-right: none; }
.parttime-day-col.active { background: #67c23a; }
.parttime-legend { margin-top: 8px; display: flex; align-items: center; gap: 6px; font-size: 12px; color: #606266; }
.parttime-legend-box { background: #67c23a; }
</style>
