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

      <el-table ref="plansTableRef" :data="plans" v-loading="plansLoading" border stripe size="small" style="margin-bottom: 16px" highlight-current-row @current-change="selectPlan">
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
        <el-date-picker v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择起始日" :disabled-date="disabledDate" @change="loadWeek" />
        <div class="gantt">
          <div class="gantt-row gantt-header">
            <div class="gantt-emp-col">员工</div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <div class="day-label">{{ weekdayName(d.weekday) }}</div><div class="day-sub">{{ d.date }}</div>
            </div>
          </div>
          <template v-for="(row, i) in sortedWeekRows" :key="row.employeeId">
            <!-- 全职员工与兼职员工之间的分隔行 -->
            <div v-if="i === firstPartTimeIndex" class="gantt-divider">
              <span class="gantt-divider-badge">兼</span>兼职员工
            </div>
            <div class="gantt-row" :class="{ 'gantt-row-parttime': row.isParttime === 1 }">
              <div class="gantt-emp-col"><div class="emp-name">{{ row.employeeName }}<el-tag v-if="row.isParttime === 1" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div class="emp-sub">{{ row.department }}</div></div>
              <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
                <template v-if="getWeekDay(row, d.date)">
                  <div v-if="getWeekDay(row, d.date).isRestDay === 1 && row.isParttime !== 1" class="day-block rest-block">休</div>
                  <div v-else-if="getWeekDay(row, d.date).isRestDay === 1" class="day-block empty-block"></div>
                  <div v-else class="day-block work-block">
                    <div class="shift-code">{{ getWeekDay(row, d.date).shiftCode || '班' }}</div>
                    <div class="shift-time">{{ fmt(getWeekDay(row, d.date).startTime) }}-{{ fmt(getWeekDay(row, d.date).endTime) }}</div>
                    <div
                      v-if="getWeekDay(row, d.date).breakStartTime && row.isParttime !== 1"
                      class="shift-break"
                      :title="getWeekDay(row, d.date).breakCoverEmployeeName
                        ? `休息 ${fmt(getWeekDay(row, d.date).breakStartTime)}-${fmt(getWeekDay(row, d.date).breakEndTime)}，由 ${getWeekDay(row, d.date).breakCoverEmployeeName} 顶班`
                        : `休息 ${fmt(getWeekDay(row, d.date).breakStartTime)}-${fmt(getWeekDay(row, d.date).breakEndTime)}`"
                    >
                      休 {{ fmt(getWeekDay(row, d.date).breakStartTime) }}-{{ fmt(getWeekDay(row, d.date).breakEndTime) }}{{ getWeekDay(row, d.date).breakCoverEmployeeName ? ' · ' + getWeekDay(row, d.date).breakCoverEmployeeName + ' 顶' : '' }}
                    </div>
                  </div>
                </template>
                <div v-else class="day-block empty-block"></div>
              </div>
            </div>
          </template>
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
                <div class="cal-parttime" v-if="day.partTimeWorkCount > 0">兼职 {{ day.partTimeWorkCount }} 上班</div>
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
            <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="(slot, si) in slots" :key="slot.key" class="m-slot-col" :class="[cellClass(ws, slot), snapTarget.ws === ws && snapTarget.slotIdx === si ? 'snap-target' : '']"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-parttime': emp.isParttime === 1, 'pt-first': isFirstPartTimeChip(emp, ws, slot), 'is-break': inBreak(emp, slot) }" :title="'[v3] 点击切换休息/上班，按住拖动可移动半小时'" @mousedown.prevent.stop="onChipMouseDown($event, emp, ws, slot)" @click.stop="onChipClick(emp, slot)"><div class="emp-name">{{ emp.employeeName }}<span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span></div><div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div><div v-else class="emp-shift break-info" :title="breakTip(emp)">休息</div></div></div></div>
          </div></div>
        </div>
      </div>

      <div v-if="viewMode === 'day'" v-loading="loading">
        <el-date-picker v-model="dayDate" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择日期" :disabled-date="disabledDate" @change="loadDay" />
        <div v-if="dayDate" class="matrix-wrap"><div class="matrix">
          <div class="m-row m-header"><div class="m-ws-col">工作站</div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display"><span v-if="isHour(slot)">{{ slot.display }}</span></div></div>
          <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="(slot, si) in slots" :key="slot.key" class="m-slot-col" :class="[cellClass(ws, slot), snapTarget.ws === ws && snapTarget.slotIdx === si ? 'snap-target' : '']"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-parttime': emp.isParttime === 1, 'pt-first': isFirstPartTimeChip(emp, ws, slot), 'is-break': inBreak(emp, slot) }" :title="'[v3] 点击切换休息/上班，按住拖动可移动半小时'" @mousedown.prevent.stop="onChipMouseDown($event, emp, ws, slot)" @click.stop="onChipClick(emp, slot)"><div class="emp-name">{{ emp.employeeName }}<span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span></div><div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div><div v-else class="emp-shift break-info" :title="breakTip(emp)">休息</div></div></div></div>
        </div></div>
      </div>

      <div v-if="viewMode === 'issues'" v-loading="issuesLoading">
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num">{{ issuesList.length }}</div><div class="stat-label">问题总数</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#f56c6c">{{ issueStats.gapCount }}</div><div class="stat-label">岗位缺口</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#e6a23c">{{ issueStats.warnCount }}</div><div class="stat-label">警告级别</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#67c23a">{{ issueStats.daysCount }}</div><div class="stat-label">影响天数</div></el-card></el-col>
        </el-row>
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="24">
            <el-card header="问题分析">
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
        </el-row>
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="24">
            <el-card header="排班合理度趋势">
              <div v-if="rationalityData.length" ref="rationalityChartRef" class="area-chart-wrap" style="width: 100%; height: 420px"></div>
            </el-card>
          </el-col>
        </el-row>
        <el-table :data="filteredIssues" border stripe size="small" max-height="400">
          <el-table-column prop="workDate" label="日期" width="110" />
          <el-table-column label="时段" width="90"><template #default="{ row }">{{ row.timeSlot ? String(row.timeSlot).substring(0, 5) : '整周期' }}</template></el-table-column>
          <el-table-column prop="workstationName" label="工作站" width="120"><template #default="{ row }">{{ row.workstationName || '--' }}</template></el-table-column>
          <el-table-column label="类型" width="110"><template #default="{ row }"><el-tag v-if="row.issueType === 'STAFFING_GAP'" type="danger" size="small">岗位缺口</el-tag><el-tag v-else-if="row.issueType === 'SKILL_MISMATCH'" type="warning" size="small">技能不匹配</el-tag><el-tag v-else-if="row.issueType === 'OVERTIME'" type="info" size="small">工时超限</el-tag><el-tag v-else-if="row.issueType === 'CONSECUTIVE_WORK'" type="info" size="small">连续工作超限</el-tag><el-tag v-else-if="row.issueType === 'BREAK_BORROW_INEXPERIENCED'" type="info" size="small">不熟练顶岗</el-tag><el-tag v-else size="small">{{ row.issueType }}</el-tag></template></el-table-column>
          <el-table-column prop="severity" label="严重度" width="80"><template #default="{ row }"><el-tag :type="row.severity === 'ERROR' ? 'danger' : (row.severity === 'INFO' ? 'info' : 'warning')" size="small">{{ row.severity === 'ERROR' ? '错误' : (row.severity === 'INFO' ? '提示' : '警告') }}</el-tag></template></el-table-column>
          <el-table-column prop="description" label="说明" min-width="280" show-overflow-tooltip />
        </el-table>
      </div>
    </el-card>

    <!-- 拖动工作段时的吸附幽灵块 -->
    <div
      v-if="dragGhost.visible"
      class="drag-ghost"
      :style="{ left: dragGhost.x + 'px', top: dragGhost.y + 'px', width: dragGhost.width + 'px', height: dragGhost.height + 'px' }"
    >{{ dragGhost.text }}</div>

    <!-- 点击色块：切换该半小时 休息/上班 -->
    <el-dialog v-model="slotStatusDialog.visible" title="切换时段状态" width="420px">
      <div class="ds-info">
        <div><span class="ds-label">员工：</span>{{ slotStatusDialog.employeeName }}（{{ slotStatusDialog.employeeNo }}）</div>
        <div><span class="ds-label">日期：</span>{{ slotStatusDialog.date }}　<span class="ds-label">时段：</span>{{ slotStatusDialog.timeSlot }} - {{ slotStatusDialog.timeSlotEnd }}</div>
      </div>
      <div style="margin: 10px 0 4px; font-size: 12px; color: #909399">
        当前状态：<el-tag size="small" :type="slotStatusDialog.currentIsBreak ? 'warning' : 'success'">{{ slotStatusDialog.currentIsBreak ? '休息' : '上班' }}</el-tag>
      </div>
      <el-radio-group v-model="slotStatusDialog.action" style="margin: 10px 0; width: 100%">
        <el-radio value="work" style="margin-right: 24px">该半小时上班</el-radio>
        <el-radio value="rest">该半小时休息</el-radio>
      </el-radio-group>
      <template #footer>
        <el-button @click="slotStatusDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="slotStatusDialog.saving" @click="submitSlotStatus">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, onBeforeUnmount, ref, reactive, computed, nextTick, watch } from 'vue'
import * as echarts from 'echarts'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getMonthView, getWeekView, getDailyView, getScheduleIssues, getScheduleRationality, getSchedules, setSlotStatus, moveScheduleSegment } from '../api/schedules'

const route = useRoute()
const planId = ref(route.query.planId || '')
const loading = ref(false)
const errorMsg = ref('')
const viewMode = ref(route.query.mode || 'week')
const plans = ref([])
const plansLoading = ref(false)
const plansTableRef = ref(null)
// 当前选中的排班方案（用于日期范围限制与日明细默认日期）
const currentPlan = ref(null)
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
const rationalityChartRef = ref(null)
let rationalityChart = null
// 周视图：低技能岗位兼职替补需求（岗位×日期 → 是否有缺口）
const weekPartTimeNeeds = ref([])
const weekPartTimeMap = ref({})

function weekdayName(i) { return ['周一','周二','周三','周四','周五','周六','周日'][i] }
function fmt(t) { return t ? String(t).substring(0, 5) : '--' }
function getWeekDay(row, date) { return row.days?.find(d => d.workDate === date) }

// 周视图：全职在前、兼职在后（同组按工号），供分隔行渲染
const sortedWeekRows = computed(() => {
  const list = [...(weekRows.value || [])]
  list.sort((a, b) => (Number(a.isParttime) - Number(b.isParttime)) || String(a.employeeNo || '').localeCompare(String(b.employeeNo || '')))
  return list
})
// 第一个兼职员工所在下标：在其前插入「兼职员工」分隔行
const firstPartTimeIndex = computed(() => sortedWeekRows.value.findIndex(r => Number(r.isParttime) === 1))

// 时间轴：13:00 为原点，每 30 分钟一段，共 34 段（13:00~次日 05:30；后端时段左闭右开，06:00 下班的班次止于 05:30）
const SLOT_COUNT = 34
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
// 单元格内员工：全职在前、兼职在后，兼职色块用绿色 + 虚线间隔与全职隔开
function dailyCellUsers(ws, slot) {
  return dailyRows.value
    .filter(r => r.workstationName === ws && String(r.timeSlot).substring(0,5) === slot.key)
    .sort((a, b) => (Number(a.isParttime ?? 0) - Number(b.isParttime ?? 0)) || (Number(a.employeeId) - Number(b.employeeId)))
}
// 是否为该单元格第一个兼职色块（且其前有全职色块）→ 显示虚线间隔
function isFirstPartTimeChip(emp, ws, slot) {
  const users = dailyCellUsers(ws, slot)
  const idx = users.findIndex(u => u.employeeId === emp.employeeId)
  const firstPt = users.findIndex(u => Number(u.isParttime) === 1)
  return firstPt === idx && users.some(u => Number(u.isParttime) === 0)
}
// P3-12: 缺口按日期过滤
function dailySlotIssues(ws, slot) {
  const currentDate = selectedDate.value || dayDate.value
  return dailyIssues.value.filter(i => i.issueType === 'STAFFING_GAP' && i.workDate === currentDate && i.workstationName === ws && i.timeSlot && String(i.timeSlot).substring(0,5) === slot.key)
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
// 休息时间段文本：20:00-20:30
function breakTimeText(row) {
  const s = String(row.breakStartTime || '').substring(0, 5)
  const e = String(row.breakEndTime || '').substring(0, 5)
  return (s && e) ? `${s}-${e}` : '--'
}
// 休息提示（仅显示休息时段）
function breakTip(row) {
  return `休息 ${breakTimeText(row)}`
}

// ============ 拖动移动工作段（时间平移 + 换工作站，吸附格子） ============
const dragMove = {
  active: false,
  moved: false,
  matrixEl: null,
  empId: 0,
  empName: '',
  shiftCode: '',
  fromWorkstationId: 0,
  segStartIdx: -1,
  segEndIdx: -1,
  targetSlotIdx: -1,
  targetWs: '',
  targetWorkstationId: 0,
  startX: 0,
  startY: 0,
  justDragged: false
}
const dragGhost = reactive({ visible: false, x: 0, y: 0, width: 0, height: 0, text: '' })
const snapTarget = reactive({ ws: '', slotIdx: -1 })

// 工作站名 → id（用于落点定位）
const wsIdByName = computed(() => {
  const m = {}
  dailyRows.value.forEach(r => {
    if (r.workstationName && r.workstationId != null) m[r.workstationName] = r.workstationId
  })
  return m
})

// 默认只移动被拖的那半个小时
function onChipMouseDown(e, emp, ws, slot) {
  if (e.button !== 0) return
  const slotIdx = slots.value.findIndex(s => s.key === slot.key)
  dragMove.active = true
  dragMove.moved = false
  dragMove.matrixEl = e.target.closest('.matrix')
  dragMove.empId = emp.employeeId
  dragMove.empName = emp.employeeName || ''
  dragMove.shiftCode = emp.shiftCode || '班'
  dragMove.fromWorkstationId = emp.workstationId
  dragMove.segStartIdx = slotIdx
  dragMove.segEndIdx = slotIdx
  dragMove.targetSlotIdx = slotIdx
  dragMove.targetWs = ws
  dragMove.targetWorkstationId = emp.workstationId
  dragMove.startX = e.clientX
  dragMove.startY = e.clientY
  dragMove.justDragged = false
  dragGhost.visible = true
  updateDragGhost(e.clientX, e.clientY)
}

// 点击（未拖动）→ 休息/上班切换
function onChipClick(emp, slot) {
  if (dragMove.justDragged) {
    dragMove.justDragged = false
    return
  }
  openSlotStatusDialog(emp, slot)
}

function updateDragGhost(clientX, clientY) {
  const matrixEl = dragMove.matrixEl
  if (!matrixEl) return
  const rows = matrixEl.querySelectorAll('.m-row:not(.m-header)')
  let rowIdx = -1
  rows.forEach((row, i) => {
    const rect = row.getBoundingClientRect()
    if (clientY >= rect.top && clientY <= rect.bottom) rowIdx = i
  })
  if (rowIdx < 0 || rowIdx >= dailyWorkstations.value.length) {
    snapTarget.ws = ''
    snapTarget.slotIdx = -1
    return
  }
  const rowRect = rows[rowIdx].getBoundingClientRect()
  const cellsLeft = rowRect.left + 130 // 左侧工作站列宽
  const slotIdx = Math.max(0, Math.min(slots.value.length - 1, Math.floor((clientX - cellsLeft) / 72)))
  const targetWs = dailyWorkstations.value[rowIdx]

  dragMove.targetSlotIdx = slotIdx
  dragMove.targetWs = targetWs
  dragMove.targetWorkstationId = wsIdByName.value[targetWs] || 0
  snapTarget.ws = targetWs
  snapTarget.slotIdx = slotIdx

  dragGhost.x = cellsLeft + slotIdx * 72 + 1
  dragGhost.y = rowRect.top + 2
  dragGhost.width = 70 // 单格（半小时）
  dragGhost.height = Math.max(24, rowRect.height - 4)
  dragGhost.text = dragMove.empName + ' · ' + dragMove.shiftCode
}

function onDragMove(e) {
  if (!dragMove.active) return
  if (Math.abs(e.clientX - dragMove.startX) > 6 || Math.abs(e.clientY - dragMove.startY) > 6) dragMove.moved = true
  updateDragGhost(e.clientX, e.clientY)
}

async function onDragEnd() {
  if (!dragMove.active) return
  const wasMoved = dragMove.moved
  dragMove.active = false
  dragGhost.visible = false
  snapTarget.ws = ''
  snapTarget.slotIdx = -1

  if (!wasMoved) {
    return // 未拖动：交给 click 事件打开休息/上班对话框
  }

  if (!planId.value) {
    ElMessage.warning('请先在上方选择排班计划，再拖动调整')
    dragMove.justDragged = true
    return
  }

  const slotList = slots.value
  const fromSlotKey = slotList[dragMove.segStartIdx].key
  const toSlotKey = slotList[dragMove.targetSlotIdx].key
  const targetUnchanged =
    toSlotKey === fromSlotKey && dragMove.targetWorkstationId === dragMove.fromWorkstationId

  if (targetUnchanged || !dragMove.targetWorkstationId || !dragMove.targetWs) {
    dragMove.justDragged = true // 拖回原位/无效落点：视为取消
    return
  }

  try {
    await moveScheduleSegment({
      planId: planId.value,
      employeeId: dragMove.empId,
      workDate: selectedDate.value || dayDate.value,
      fromWorkstationId: dragMove.fromWorkstationId,
      fromTimeSlot: fromSlotKey,
      toTimeSlot: toSlotKey,
      toWorkstationId: dragMove.targetWorkstationId
    })
    ElMessage.success('已移动 ' + dragMove.empName + ' 的半小时 → ' + dragMove.targetWs + ' ' + toSlotKey)
    await loadDay()
  } catch (err) {
    // request 拦截器已提示错误
  }
}

// ===== 点击色块：切换该半小时 休息/上班 =====  //
const slotStatusDialog = reactive({
  visible: false,
  employeeId: null,
  employeeName: '',
  employeeNo: '',
  date: '',
  timeSlot: '',
  timeSlotEnd: '',
  currentIsBreak: false,
  action: 'work', // work | rest
  saving: false
})

// 半小时 +30 分钟（跨午夜按 24 小时回绕）
function slotEnd(time) {
  const parts = String(time).split(':').map(Number)
  const total = (parts[0] * 60 + (parts[1] || 0) + 30) % 1440
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
}

// 打开弹窗：默认动作与当前状态相反（休息→上班，上班→休息）
function openSlotStatusDialog(emp, slot) {
  const date = selectedDate.value || dayDate.value
  if (!date) return
  const isBreak = inBreak(emp, slot)
  slotStatusDialog.employeeId = emp.employeeId
  slotStatusDialog.employeeName = emp.employeeName || ''
  slotStatusDialog.employeeNo = emp.employeeNo || ''
  slotStatusDialog.date = date
  slotStatusDialog.timeSlot = slot.key
  slotStatusDialog.timeSlotEnd = slotEnd(slot.key)
  slotStatusDialog.currentIsBreak = isBreak
  slotStatusDialog.action = isBreak ? 'work' : 'rest'
  slotStatusDialog.saving = false
  slotStatusDialog.visible = true
}

async function submitSlotStatus() {
  const d = slotStatusDialog
  const isRest = d.action === 'rest'
  if (!planId.value || !d.employeeId || !d.date || !d.timeSlot) return
  d.saving = true
  try {
    await setSlotStatus(planId.value, {
      items: [{
        employeeId: d.employeeId,
        workDate: d.date,
        timeSlot: d.timeSlot + ':00',
        isRest: isRest ? 1 : 0
      }]
    })
    ElMessage.success(isRest
      ? `已改为休息（${d.timeSlot} - ${d.timeSlotEnd}）`
      : `已恢复上班（${d.timeSlot} - ${d.timeSlotEnd}）`)
    d.visible = false
    // 刷新日明细与月视图（休息计数）
    const date = selectedDate.value || dayDate.value
    await loadDay(date)
    try {
      monthRows.value = await getMonthView(planId.value)
      if (viewMode.value === 'month') buildCalendar()
    } catch {}
  } catch {
    // 失败提示已由 request 拦截器统一弹出
  } finally {
    d.saving = false
  }
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
    // 不展示“无人顶岗”类问题（休息无人顶岗提示整体下线）
    issuesList.value = issues.filter(i => i.issueType !== 'BREAK_UNCOVERED')
    // 优先使用后端 /rationality 端点；404/失败时回退从缺口描述解析（兼容未升级的后端）
    try {
      const rationality = await getScheduleRationality(planId.value)
      rationalityList.value = rationality || []
    } catch (e) {
      rationalityList.value = computeRationalityFromIssues(issues)
    }
  } catch (e) {
    /* 拦截器已提示 */
  } finally { issuesLoading.value = false }
}

function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 当前选中排班方案的日期范围（YYYY-MM-DD）
function planRange() {
  if (!currentPlan.value?.startDate || !currentPlan.value?.endDate) return null
  return { start: currentPlan.value.startDate, end: currentPlan.value.endDate }
}

// 日期选择器限制：只能选该排班方案周期内的日期
function disabledDate(date) {
  const range = planRange()
  if (!range) return false
  const start = new Date(range.start + 'T00:00:00')
  const end = new Date(range.end + 'T00:00:00')
  return date < start || date > end
}

async function loadWeek() {
  if (!planId.value) { errorMsg.value = '请输入计划ID'; return }
  if (!weekStart.value) { weekStart.value = currentPlan.value?.startDate || getToday() }
  weekRows.value = await getWeekView(planId.value, weekStart.value)
  // 并集所有行的日期，避免只取第一行而遗漏其他员工的班次日期
  const dateSet = new Set()
  weekRows.value.forEach(r => {
    (r.days || []).forEach(d => {
      if (d && d.workDate) dateSet.add(d.workDate)
    })
  })
  weekDays.value = Array.from(dateSet).sort().map(date => {
    const dow = new Date(date + 'T00:00:00').getDay()
    return { date, weekday: dow === 0 ? 6 : dow - 1 }
  })

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

async function loadDay(date) {
  if (!planId.value) return
  let d = date || dayDate.value
  const range = planRange()
  // 自动筛选当前方案：日期缺省或不在方案周期内时，回落到今天（不在周期内则取方案开始日）
  if (!d || (range && (d < range.start || d > range.end))) {
    if (date) return
    const today = getToday()
    d = range ? (today >= range.start && today <= range.end ? today : range.start) : ''
    dayDate.value = d
    if (!d) return
  }
  dailyLoading.value = true
  try {
    // 同时拉取月视图数据：用于矩阵下方的「休息员工」区
    const [res, iss, month] = await Promise.all([
      getDailyView(planId.value, d),
      getScheduleIssues(planId.value),
      getMonthView(planId.value)
    ])
    dailyRows.value = res || []
    dailyIssues.value = iss || []
    if (Array.isArray(month)) monthRows.value = month
  } catch (e) {
    /* 拦截器已提示 */
  } finally { dailyLoading.value = false }
}

async function selectDate(date) { selectedDate.value = date; await loadDay(date) }

function onModeChange() {
  selectedDate.value = ''
  dailyRows.value = []
  // 切到日明细时：默认日期限定在当前方案的周期内
  if (viewMode.value === 'day') {
    const range = planRange()
    if (!dayDate.value || (range && (dayDate.value < range.start || dayDate.value > range.end))) {
      const today = getToday()
      dayDate.value = range ? (today >= range.start && today <= range.end ? today : range.start) : ''
    }
  }
  loadAll()
}

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
  const stats = {}; days.forEach(d => { stats[d] = { w: 0, r: 0, pt: 0, s: new Set() } })
  monthRows.value.forEach(row => row.days.forEach(d => {
    if (d.isRestDay === 1) {
      // 兼职员工空闲日不算休息（排班界面直接留空）
      if (Number(row.isParttime) !== 1) stats[d.workDate].r++
    } else {
      stats[d.workDate].w++
      // 兼职上班人数单独统计，月历里与全职分开显示
      if (Number(row.isParttime) === 1) stats[d.workDate].pt++
      if (d.shiftCode) stats[d.workDate].s.add(d.shiftCode)
    }
  }))
  const fd = new Date(days[0] + 'T00:00:00'), ld = new Date(days[days.length - 1] + 'T00:00:00')
  const cs = new Date(fd); cs.setDate(fd.getDate() - ((fd.getDay() + 6) % 7))
  const ce = new Date(ld); ce.setDate(ld.getDate() + (6 - (ld.getDay() + 6) % 7))
  const ws = []; const c = new Date(cs)
  while (c <= ce) {
    const w = []
    for (let i = 0; i < 7; i++) {
      const k = `${c.getFullYear()}-${String(c.getMonth()+1).padStart(2,'0')}-${String(c.getDate()).padStart(2,'0')}`
      if (c >= fd && c <= ld && stats[k]) { const s = stats[k]; w.push({ date: k, dayNum: c.getDate(), workCount: s.w, partTimeWorkCount: s.pt, restCount: s.r, shiftSummary: Array.from(s.s).slice(0,3).join(' ') }) }
      else w.push({ date: '', dayNum: '', workCount: 0, partTimeWorkCount: 0, restCount: 0, shiftSummary: '' })
      c.setDate(c.getDate() + 1)
    }
    ws.push(w)
  }
  weeks.value = ws
}

// P3-39: 切换计划时重置所有状态
function selectPlan(row) {
  if (!row) return
  currentPlan.value = row
  planId.value = row.id
  weekStart.value = row.startDate || ''
  selectedDate.value = ''
  dayDate.value = ''
  dailyRows.value = []
  dailyIssues.value = []
  monthRows.value = []
  issuesList.value = []
  // 保持当前视图，各视图按新方案自动重新加载；日明细日期会自动落到新方案周期内
  loadAll()
}

async function loadPlans() {
  plansLoading.value = true
  try {
    const res = await getSchedules({ page: 1, pageSize: 100 })
    plans.value = res.items || []
    // 自动选中当前 planId 对应的排班方案：高亮表格行并记录其周期范围
    if (planId.value) {
      const matched = plans.value.find(p => String(p.id) === String(planId.value))
      if (matched) {
        currentPlan.value = matched
        nextTick(() => plansTableRef.value?.setCurrentRow(matched))
      }
    }
  } catch (e) {
    /* 拦截器已提示 */
  } finally { plansLoading.value = false }
}

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

function filterTableByWs(wsName) { wsFilter.value = wsFilter.value === wsName ? '' : wsName }
function filterTableByType(type) { typeFilter.value = typeFilter.value === type ? '' : type }

const filteredIssues = computed(() => {
  let list = issuesList.value
  if (wsFilter.value) list = list.filter(i => (i.workstationName || '未知') === wsFilter.value)
  if (typeFilter.value) list = list.filter(i => i.issueType === typeFilter.value)
  return list
})

// 合理度 = 已满足需求 ÷ 总需求 × 100（量纲统一为“人”）
// 合理度：使用后端返回的每日 Rationality（实际排班覆盖 ÷ 需求）
const rationalityData = computed(() => {
  if (rationalityList.value && rationalityList.value.length) {
    return rationalityList.value.map(r => ({ date: r.date, pct: r.pct }))
  }
  return []
})
function renderRationalityChart() {
  if (!rationalityChartRef.value || rationalityData.value.length === 0) return
  if (!rationalityChart) {
    rationalityChart = echarts.init(rationalityChartRef.value)
  }
  const dates = rationalityData.value.map(r => r.date.substring(5))
  const values = rationalityData.value.map(r => r.pct)
  rationalityChart.setOption({
    grid: { left: 45, right: 20, top: 20, bottom: 30 },
    tooltip: { trigger: 'axis', formatter: params => {
      const p = params[0]
      return rationalityData.value[p.dataIndex].date + '<br/>合理度：' + p.value + '%'
    }},
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLabel: { fontSize: 12, fontWeight: 'bold', color: '#303133' }
    },
    yAxis: {
      type: 'value',
      min: 0,
      max: 100,
      axisLabel: { fontSize: 12, fontWeight: 'bold', color: '#606266', formatter: '{value}%' },
      splitLine: { lineStyle: { color: '#ebeef5' } }
    },
    series: [{
      type: 'line',
      data: values,
      smooth: true,
      symbol: 'circle',
      symbolSize: 8,
      lineStyle: { width: 3, color: '#409eff' },
      itemStyle: { color: '#409eff', borderColor: '#fff', borderWidth: 2 },
      areaStyle: { color: 'rgba(64,158,255,0.15)' },
      label: { show: true, fontSize: 12, fontWeight: 'bold', color: '#303133', formatter: '{c}%' }
    }]
  })
}

watch(rationalityData, () => {
  nextTick(() => renderRationalityChart())
})

onMounted(() => {
  loadPlans()
  if (planId.value) loadAll()
  nextTick(() => renderRationalityChart())
  window.addEventListener('mousemove', onDragMove)
  window.addEventListener('mouseup', onDragEnd)
})

onBeforeUnmount(() => {
  window.removeEventListener('mousemove', onDragMove)
  window.removeEventListener('mouseup', onDragEnd)
  if (rationalityChart) {
    rationalityChart.dispose()
    rationalityChart = null
  }
})
</script>

<style scoped>
.gantt { border: 1px solid #ebeef5; border-radius: 4px; overflow-x: auto; }
.gantt-row { display: flex; border-bottom: 1px solid #ebeef5; min-width: max-content; }
.gantt-row-parttime .gantt-emp-col { background: #f7fdf5; }
.gantt-divider { background: #f0f9eb; color: #67c23a; font-weight: 600; font-size: 12px; padding: 5px 10px; border-bottom: 1px solid #c2e7b0; display: flex; align-items: center; gap: 6px; }
.gantt-divider-badge { display: inline-block; background: #67c23a; color: #fff; border-radius: 3px; font-size: 11px; padding: 0 5px; line-height: 16px; }
.gantt-header { background: #f5f7fa; font-weight: 600; }
.gantt-emp-col { width: 140px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; position: sticky; left: 0; background: #fff; z-index: 3; }
.gantt-header .gantt-emp-col { background: #f5f7fa; z-index: 4; }
.gantt-day-col { flex: 1; min-width: 100px; padding: 4px; border-right: 1px solid #ebeef5; }
.day-label { font-size: 12px; color: #606266; }
.day-sub { font-size: 11px; color: #909399; }
.day-block { border-radius: 4px; padding: 6px; text-align: center; font-size: 12px; min-height: 40px; }
.work-block { background: #ecf5ff; }
.rest-block { background: #fef0f0; color: #f56c6c; font-weight: 600; }
.empty-block { background: #fafafa; }
.shift-code { font-weight: 600; }
.shift-time { font-size: 11px; color: #909399; }
.shift-break { margin-top: 2px; font-size: 10px; color: #e6a23c; line-height: 1.4; }
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
.cal-parttime { font-size: 12px; color: #67c23a; }
.cal-rest { font-size: 12px; color: #f56c6c; }
.cal-shift { font-size: 11px; color: #909399; margin-top: 4px; }
.matrix-wrap { overflow: auto; max-height: 560px; }
.matrix { border: 1px solid #ebeef5; border-radius: 4px; }
.m-row { display: flex; border-bottom: 1px solid #ebeef5; }
.m-row:last-child { border-bottom: none; }
.m-header { background: #f5f7fa; font-weight: 600; position: sticky; top: 0; z-index: 4; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; display: flex; align-items: center; position: sticky; left: 0; background: #fff; z-index: 3; }
.m-header .m-ws-col { background: #f5f7fa; z-index: 5; }
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
/* 兼职色块：绿色，且与前面的全职色块用虚线间隔隔开 */
.emp-chip.is-parttime { background: #67c23a; }
.emp-chip.pt-first { border-top: 1px dashed #a3d98a; padding-top: 3px; margin-top: 1px; }
/* 班中休息色块：灰色 + 橙色「休」标记，第二行显示休息时间段与顶岗人 */
.emp-chip.is-break { background: #909399; }
.emp-chip.is-break .break-info { color: #ffe6a7; }
.break-flag { display: inline-block; background: #e6a23c; color: #fff; border-radius: 2px; padding: 0 3px; margin-left: 4px; font-size: 10px; line-height: 14px; }
.next-day .emp-chip { background: #e6a23c; }
.next-day .emp-chip.is-parttime { background: #67c23a; }
.next-day .emp-chip.is-break { background: #909399; }
/* 色块可点击：切换该半小时 休息/上班 */
.emp-chip { cursor: grab; }
.emp-chip:hover { opacity: 0.85; }

/* ============ 拖动移动工作段 ============ */
.drag-ghost {
  position: fixed;
  z-index: 3000;
  pointer-events: none;
  background: rgba(64, 158, 255, 0.78);
  color: #fff;
  border: 1px dashed #fff;
  border-radius: 3px;
  font-size: 11px;
  padding: 3px 8px;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.28);
  display: flex;
  align-items: center;
  overflow: hidden;
  white-space: nowrap;
}
.m-slot-col.snap-target {
  outline: 2px dashed #409eff;
  outline-offset: -2px;
  background: rgba(64, 158, 255, 0.14) !important;
}

.ds-info { font-size: 13px; color: #303133; }
.ds-label { color: #909399; }
.stat-num { font-size: 28px; font-weight: 700; color: #303133; }
.stat-label { font-size: 13px; color: #909399; margin-top: 4px; }
.legend-row { display: flex; align-items: center; gap: 6px; font-size: 12px; }
.legend-row.clickable { cursor: pointer; user-select: none; }
.legend-row.clickable:hover { background: #f5f7fa; border-radius: 4px; }
.legend-dot { width: 12px; height: 12px; border-radius: 50%; display: inline-block; }
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
