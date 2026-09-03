<template>
  <div class="mgr-schedule-page">
    <van-nav-bar title="全店排班" />

    <!-- 计划选择 -->
    <van-dropdown-menu>
      <van-dropdown-item v-model="planId" :options="planOptions" @change="onPlanChange" />
    </van-dropdown-menu>

    <!-- 计划信息条 -->
    <div class="plan-bar" v-if="currentPlan">
      <van-tag :type="currentPlan.status === 'PUBLISHED' ? 'success' : 'warning'" plain>
        {{ currentPlan.status === 'PUBLISHED' ? '已发布' : '草稿' }}
      </van-tag>
      <span class="plan-range">{{ currentPlan.startDate }} ~ {{ currentPlan.endDate }}</span>
      <van-button
        v-if="currentPlan.status === 'DRAFT'"
        size="small"
        round
        type="primary"
        plain
        :loading="acting"
        @click="publishFlow"
      >发布</van-button>
      <van-button
        v-else
        size="small"
        round
        type="danger"
        plain
        :loading="acting"
        @click="unpublishFlow"
      >退回</van-button>
    </div>
    <div class="draft-tip" v-if="currentPlan && currentPlan.status !== 'PUBLISHED'">
      当前为草稿计划，员工端不可见；发布后生效<template v-if="currentPlan.issueCount">（存在问题 {{ currentPlan.issueCount }} 条）</template>
    </div>

    <!-- iOS 风格分段切换器：周 / 月 / 日 -->
    <div class="segmented">
      <span class="seg" :class="{ 'seg-active': viewTab === 'week' }" @click="viewTab = 'week'">周</span>
      <span class="seg" :class="{ 'seg-active': viewTab === 'month' }" @click="viewTab = 'month'">月</span>
      <span class="seg" :class="{ 'seg-active': viewTab === 'day' }" @click="viewTab = 'day'">日</span>
    </div>

    <!-- ============ 周视图：全员安排总表 ============ -->
    <div v-show="viewTab === 'week'" class="view-pane">
      <div class="view-toolbar">
        <van-icon name="arrow-left" @click="moveWeek(-1)" />
        <span class="view-range">{{ weekRangeLabel }}</span>
        <van-icon name="arrow" @click="moveWeek(1)" />
      </div>
      <div class="range-note" v-if="weekOutOfPlan">该周不在「{{ currentPlan?.planName }}」范围内，无排班数据</div>
      <div class="week-summary" v-if="!weekLoading">
        本周：上班 {{ weekWorkTotal }} 人次 · 休息 {{ weekRestTotal }} 人次
      </div>

      <van-loading v-if="weekLoading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <div v-else class="week-scroll">
        <table class="week-grid">
          <thead>
            <tr>
              <th class="emp-col">员工</th>
              <th v-for="d in weekDays" :key="d.date" class="day-head" :class="{ 'is-today': d.date === todayStr }" @click="openDay(d.date)">
                <div class="dh-dow" :class="{ 'is-today': d.date === todayStr }">{{ d.dow }}</div>
                <div class="dh-num" :class="{ 'today-circle': d.date === todayStr }">{{ d.num }}</div>
                <div class="dh-cnt" v-if="weekStats(d.date).work || weekStats(d.date).rest">
                  {{ weekStats(d.date).work }}/{{ weekStats(d.date).rest }}
                </div>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="emp in weekRows" :key="emp.employeeId">
              <td class="emp-col">
                <div class="emp-name">{{ emp.employeeName }}</div>
                <div class="emp-no">{{ emp.employeeNo }}<template v-if="emp.isParttime === 1"> · 兼</template></div>
              </td>
              <td
                v-for="d in weekDays"
                :key="d.date"
                class="day-cell"
                :class="{ 'is-today': d.date === todayStr }"
                @click="openDay(d.date)"
              >
                <template v-if="weekCellFor(emp, d.date)">
                  <div class="cell-shift" v-if="weekCellFor(emp, d.date).isRestDay !== 1">
                    {{ weekCellFor(emp, d.date).shiftCode || '班' }}
                  </div>
                  <div class="cell-time" v-if="weekCellFor(emp, d.date).isRestDay !== 1">
                    {{ fmtTime(weekCellFor(emp, d.date).startTime) }}-{{ fmtTime(weekCellFor(emp, d.date).endTime) }}
                  </div>
                  <div class="cell-rest" v-else>休</div>
                </template>
                <div v-else class="cell-none">·</div>
              </td>
            </tr>
          </tbody>
        </table>
        <van-empty v-if="!weekRows.length" description="本周暂无排班数据" image-size="72" />
      </div>
    </div>

    <!-- ============ 月视图：日历样式 ============ -->
    <div v-show="viewTab === 'month'" class="view-pane">
      <div class="view-toolbar">
        <van-icon name="arrow-left" @click="moveMonth(-1)" />
        <span class="view-range">{{ monthCursorLabel }}</span>
        <van-icon name="arrow" @click="moveMonth(1)" />
      </div>
      <div class="range-note" v-if="monthOutOfPlan">该月不在「{{ currentPlan?.planName }}」范围内，无排班数据</div>

      <van-loading v-if="monthLoading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <div v-else class="cal-grid">
        <div class="cal-weekdays">
          <span v-for="w in ['一', '二', '三', '四', '五', '六', '日']" :key="w">{{ w }}</span>
        </div>
        <div class="cal-week" v-for="(week, wi) in monthWeeks" :key="wi">
          <div
            v-for="cell in week"
            :key="cell.date"
            class="cal-cell"
            :class="{ 'is-today': cell.isToday, 'is-out': !cell.inMonth }"
            @click="openDay(cell.date)"
          >
            <span class="cal-num" :class="{ 'today-circle': cell.isToday }">{{ cell.num }}</span>
            <template v-if="cell.inMonth && cell.hasData">
              <span class="cal-count cal-work">{{ cell.work }}人上班</span>
              <span class="cal-count cal-rest">{{ cell.rest }}人休</span>
            </template>
            <span v-else-if="!cell.inMonth" class="cal-nodata">·</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ============ 日视图：当天日明细 ============ -->
    <div v-show="viewTab === 'day'" class="view-pane">
      <div class="view-toolbar">
        <van-icon name="arrow-left" @click="moveDay(-1)" />
        <span class="view-range day-range">{{ dayCursorLabel }}</span>
        <van-icon name="arrow" @click="moveDay(1)" />
      </div>
      <div class="range-note" v-if="dayOutOfPlan">该日期不在「{{ currentPlan?.planName }}」范围内，无排班数据</div>

      <van-loading v-if="dayLoading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <template v-else>
        <div class="day-list">
          <div v-for="g in dayRows" :key="g.employeeId" class="day-item">
            <div class="di-head">
              <span class="di-name">{{ g.name }}</span>
              <span class="di-no">{{ g.no }}<template v-if="g.isParttime === 1"> · 兼职</template></span>
              <span class="di-shift">{{ g.shiftCode || '临时班' }}</span>
            </div>
            <div class="di-time">{{ fmtTime(g.start) }} - <template v-if="g.crossDay">次日</template>{{ fmtTime(g.end) }}<template v-if="g.workstations"> · {{ g.workstations }}</template></div>
            <div class="di-break" v-if="g.breakStart">
              休 {{ fmtTime(g.breakStart) }}-{{ fmtTime(g.breakEnd) }}<template v-if="g.breakCover">（{{ g.breakCover }} 顶班）</template>
            </div>
          </div>
          <van-empty v-if="!dayRows.length" description="当天无上班人员" image-size="72" />
        </div>
        <div v-if="restNames.length" class="day-rest">
          <div class="dr-title">休息 {{ restNames.length }} 人</div>
          <div class="dr-names">{{ restNames.join('、') }}</div>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { showSuccessToast, showConfirmDialog } from 'vant'
import {
  getPlans,
  getWeekView,
  getMonthView,
  getDailyView,
  getIssues,
  publishPlan,
  unpublishPlan
} from '../../api/schedules'

const todayStr = dayjs().format('YYYY-MM-DD')
const viewTab = ref('week')
const acting = ref(false)

const plans = ref([])
const planId = ref(null)
const planOptions = computed(() =>
  plans.value.map((p) => ({
    text: p.planName + (p.status === 'PUBLISHED' ? '（已发布）' : '（草稿）'),
    value: p.id
  }))
)
const currentPlan = computed(() => plans.value.find((p) => p.id === planId.value) || null)

const weekRows = ref([])
const weekLoading = ref(false)
const weekStart = ref(mondayOf(dayjs()))

const monthData = ref([])
const monthLoading = ref(false)
const monthCursor = ref(dayjs().format('YYYY-MM'))

const dayCursor = ref(dayjs())
const dayRows = ref([])
const dayLoading = ref(false)

function mondayOf(d) {
  const x = dayjs(d)
  return x.subtract((x.day() + 6) % 7, 'day')
}

function dstr(d) {
  return d.format('YYYY-MM-DD')
}

function fmtTime(t) {
  if (!t) return '--'
  const m = String(t).match(/^\d{2}:\d{2}/)
  return m ? m[0] : '--'
}

function minutes(t) {
  const m = String(t || '').match(/^(\d{2}):(\d{2})/)
  return m ? Number(m[1]) * 60 + Number(m[2]) : 0
}

function fmtMinutes(min) {
  const h = Math.floor(min / 60)
  const mm = min % 60
  return String(h).padStart(2, '0') + ':' + String(mm).padStart(2, '0')
}

// ---------- 计划与数据加载 ----------
async function loadPlans() {
  try {
    const data = await getPlans({ page: 1, pageSize: 50 })
    const items = data?.items || []
    items.sort((a, b) => String(b.startDate).localeCompare(String(a.startDate)))
    plans.value = items
    if (!planId.value && items.length) {
      const covering = items.find((p) => p.startDate <= todayStr && p.endDate >= todayStr)
      planId.value = covering ? covering.id : items[0].id
    }
  } catch (e) {
    // 拦截器已提示
  }
}

async function loadWeek() {
  if (!planId.value) return
  weekLoading.value = true
  try {
    weekRows.value = await getWeekView(planId.value, dstr(weekStart.value))
  } catch (e) {
    weekRows.value = []
  } finally {
    weekLoading.value = false
  }
}

async function loadMonth() {
  if (!planId.value) return
  monthLoading.value = true
  try {
    monthData.value = await getMonthView(planId.value)
  } catch (e) {
    monthData.value = []
  } finally {
    monthLoading.value = false
  }
}

async function loadDay() {
  if (!planId.value) return
  dayLoading.value = true
  try {
    const data = await getDailyView(planId.value, dstr(dayCursor.value))
    dayRows.value = groupDaily(data?.rows || [])
  } catch (e) {
    dayRows.value = []
  } finally {
    dayLoading.value = false
  }
}

async function onPlanChange() {
  const p = currentPlan.value
  if (!p) return
  const anchor = p.startDate <= todayStr && p.endDate >= todayStr ? dayjs(todayStr) : dayjs(p.startDate)
  weekStart.value = mondayOf(anchor)
  monthCursor.value = anchor.format('YYYY-MM')
  dayCursor.value = anchor
  await Promise.all([loadWeek(), loadMonth(), loadDay()])
}

// ---------- 周视图 ----------
const weekDays = computed(() =>
  Array.from({ length: 7 }, (_, i) => {
    const d = weekStart.value.add(i, 'day')
    const date = dstr(d)
    const dow = d.day() === 0 ? 6 : d.day() - 1
    return { date, dow: '周' + '一二三四五六日'[dow], num: d.date() }
  })
)

// 某天的 上班/休息 人数（基于周数据）
const weekStatsCache = computed(() => {
  const m = new Map()
  for (const emp of weekRows.value) {
    for (const d of emp.days || []) {
      const date = String(d.workDate)
      if (!m.has(date)) m.set(date, { work: 0, rest: 0 })
      if (d.isRestDay === 1) m.get(date).rest++
      else m.get(date).work++
    }
  }
  return m
})

function weekStats(date) {
  return weekStatsCache.value.get(date) || { work: 0, rest: 0 }
}

function weekCellFor(emp, date) {
  return (emp.days || []).find((d) => String(d.workDate) === date) || null
}

const weekWorkTotal = computed(() =>
  weekDays.value.reduce((sum, d) => sum + weekStats(d.date).work, 0)
)

const weekRestTotal = computed(() =>
  weekDays.value.reduce((sum, d) => sum + weekStats(d.date).rest, 0)
)

const weekRangeLabel = computed(() => {
  const s = weekStart.value
  const e = s.add(6, 'day')
  return s.month() + 1 + '月' + s.date() + '日 - ' + (e.month() + 1) + '月' + e.date() + '日'
})

function moveWeek(delta) {
  weekStart.value = weekStart.value.add(delta * 7, 'day')
  loadWeek()
}

// ---------- 月视图 ----------
// 每天 上班/休息 人数（基于月数据）
const monthStatsMap = computed(() => {
  const m = new Map()
  for (const emp of monthData.value) {
    for (const d of emp.days || []) {
      const date = String(d.workDate)
      if (!m.has(date)) m.set(date, { work: 0, rest: 0 })
      if (d.isRestDay === 1) m.get(date).rest++
      else m.get(date).work++
    }
  }
  return m
})

const monthWeeks = computed(() => {
  const first = dayjs(monthCursor.value + '-01')
  const gridStart = mondayOf(first)
  const weeks = []
  for (let w = 0; w < 6; w++) {
    const row = []
    for (let i = 0; i < 7; i++) {
      const d = gridStart.add(w * 7 + i, 'day')
      const date = dstr(d)
      const stats = monthStatsMap.value.get(date)
      row.push({
        date,
        num: d.date(),
        inMonth: d.month() === first.month(),
        isToday: date === todayStr,
        hasData: !!stats && (stats.work > 0 || stats.rest > 0),
        work: stats ? stats.work : 0,
        rest: stats ? stats.rest : 0
      })
    }
    weeks.push(row)
  }
  return weeks
})

const monthCursorLabel = computed(() => {
  const d = dayjs(monthCursor.value + '-01')
  return d.year() + '年' + (d.month() + 1) + '月'
})

function moveMonth(delta) {
  monthCursor.value = dayjs(monthCursor.value + '-01')
    .add(delta, 'month')
    .format('YYYY-MM')
}

// ---------- 日视图 ----------
const dayCursorLabel = computed(() => {
  const d = dayCursor.value
  const dow = d.day() === 0 ? 6 : d.day() - 1
  return d.month() + 1 + '月' + d.date() + '日 周' + '一二三四五六日'[dow]
})

const restNames = computed(() => {
  const date = dstr(dayCursor.value)
  const names = []
  for (const emp of monthData.value) {
    const d = (emp.days || []).find((x) => String(x.workDate) === date)
    if (d && d.isRestDay === 1) names.push(emp.employeeName)
  }
  return names
})

function moveDay(delta) {
  dayCursor.value = dayCursor.value.add(delta, 'day')
  loadDay()
}

function groupDaily(rows) {
  const map = new Map()
  for (const r of rows) {
    if (!map.has(r.employeeId)) {
      map.set(r.employeeId, {
        employeeId: r.employeeId,
        name: r.employeeName,
        no: r.employeeNo,
        isParttime: r.isParttime,
        shiftCode: r.shiftCode,
        slots: [],
        workstations: new Set(),
        breakStart: r.breakStartTime,
        breakEnd: r.breakEndTime,
        breakCover: r.breakCoverEmployeeName
      })
    }
    const g = map.get(r.employeeId)
    g.slots.push(r.timeSlot)
    if (r.workstationName) g.workstations.add(r.workstationName)
    if (!g.shiftCode && r.shiftCode) g.shiftCode = r.shiftCode
    if (!g.breakStart && r.breakStartTime) {
      g.breakStart = r.breakStartTime
      g.breakEnd = r.breakEndTime
      g.breakCover = r.breakCoverEmployeeName
    }
  }
  return [...map.values()].map((g) => {
    // 营业日时间轴排序：06:00 前的时段属于当天末尾（跨午夜班的尾部），排到最后
    const ordered = [...g.slots].sort((a, b) => {
      const ma = minutes(a) >= 360 ? minutes(a) : minutes(a) + 1440
      const mb = minutes(b) >= 360 ? minutes(b) : minutes(b) + 1440
      return ma - mb
    })
    const first = ordered[0]
    const last = ordered[ordered.length - 1]
    const hasEarly = ordered.some((t) => minutes(t) < 360)
    const hasMain = ordered.some((t) => minutes(t) >= 360)
    return {
      ...g,
      start: fmtMinutes(minutes(first)),
      end: fmtMinutes(minutes(last) + 30),
      // 跨午夜班（如 19:00-次日04:00）：既有正段又有凌晨尾段
      crossDay: hasEarly && hasMain,
      workstations: [...g.workstations].join('、')
    }
  })
}

// 周/月视图点击某天 → 跳转日视图
function openDay(date) {
  if (!date) return
  dayCursor.value = dayjs(date)
  viewTab.value = 'day'
  loadDay()
}

// ---------- 越界提示（自由翻页后仍可知道是否在计划内） ----------
const weekOutOfPlan = computed(() => {
  const p = currentPlan.value
  if (!p) return false
  const s = dstr(weekStart.value)
  const e = dstr(weekStart.value.add(6, 'day'))
  return s < p.startDate || e > p.endDate
})

const monthOutOfPlan = computed(() => {
  const p = currentPlan.value
  if (!p) return false
  const first = dayjs(monthCursor.value + '-01')
  const last = first.add(1, 'month').subtract(1, 'day')
  return first.isAfter(dayjs(p.endDate)) || last.isBefore(dayjs(p.startDate))
})

const dayOutOfPlan = computed(() => {
  const p = currentPlan.value
  if (!p) return false
  const d = dstr(dayCursor.value)
  return d < p.startDate || d > p.endDate
})

// ---------- 发布 / 退回 ----------
async function publishFlow() {
  if (acting.value) return
  const plan = currentPlan.value
  if (!plan) return

  let issues = []
  try {
    issues = await getIssues(plan.id)
  } catch (e) {
    issues = []
  }
  const errCount = issues.filter((x) => x.severity === 'ERROR').length
  const warnCount = issues.filter((x) => x.severity !== 'ERROR').length

  let message = '发布后员工将收到班表通知，员工端即可查看。确认发布？'
  let confirmText = '发布'
  if (errCount > 0) {
    message =
      '该计划存在 ' + errCount + ' 条严重违规（ERROR）' +
      (warnCount > 0 ? '、' + warnCount + ' 条提醒' : '') +
      '，强制发布可能导致班表不合规。仍要强制发布？'
    confirmText = '强制发布'
  }
  try {
    await showConfirmDialog({ title: '确认发布', message, confirmButtonText: confirmText })
  } catch (e) {
    return
  }

  acting.value = true
  try {
    await publishPlan(plan.id, errCount > 0)
    showSuccessToast('排班发布成功')
    await loadPlans()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    acting.value = false
  }
}

async function unpublishFlow() {
  if (acting.value) return
  const plan = currentPlan.value
  if (!plan) return
  try {
    await showConfirmDialog({
      title: '退回草稿',
      message: '退回后员工端班表将失效，员工会收到「排班已取消」通知。确认退回？',
      confirmButtonText: '退回'
    })
  } catch (e) {
    return
  }
  acting.value = true
  try {
    await unpublishPlan(plan.id)
    showSuccessToast('已退回草稿')
    await loadPlans()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    acting.value = false
  }
}

onMounted(async () => {
  await loadPlans()
  await Promise.all([loadWeek(), loadMonth(), loadDay()])
})
</script>

<style scoped>
/* ===== 苹果日历风格基色 ===== */
.mgr-schedule-page {
  min-height: 100vh;
  background: #f2f2f7;
  --cal-red: #ff3b30;
  --cal-blue: #007aff;
  --cal-orange: #ff9500;
  --cal-text: #1c1c1e;
  --cal-sub: #8e8e93;
  --cal-line: #e5e5ea;
}

.plan-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px 0;
  font-size: 12px;
  color: var(--cal-sub);
  background: #fff;
}

.plan-range {
  flex: 1;
  color: var(--cal-sub);
}

.draft-tip {
  margin: 6px 16px 0;
  font-size: 12px;
  color: var(--cal-orange);
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

/* iOS 分段切换器 */
.segmented {
  display: flex;
  margin: 12px 16px 4px;
  background: #e9e9eb;
  border-radius: 9px;
  padding: 2px;
}

.seg {
  flex: 1;
  text-align: center;
  padding: 5px 0;
  font-size: 13px;
  color: var(--cal-text);
  border-radius: 7px;
  transition: background 0.15s ease;
}

.seg-active {
  background: #fff;
  font-weight: 600;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
}

.view-pane {
  padding-bottom: 16px;
}

.view-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px 8px;
}

/* 加大翻页箭头，方便点击且更醒目 */
.view-toolbar .van-icon {
  font-size: 22px;
  color: var(--cal-blue);
  padding: 4px;
}

.range-note {
  margin: 0 20px 8px;
  font-size: 12px;
  color: var(--cal-sub);
}

.view-range {
  font-size: 16px;
  font-weight: 700;
  color: var(--cal-text);
}

.today-circle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 50%;
  background: var(--cal-red);
  color: #fff !important;
  font-weight: 600;
  font-size: 13px;
}

/* ===== 周视图：全员安排总表 ===== */
.week-summary {
  margin: 0 20px 8px;
  font-size: 12px;
  color: var(--cal-sub);
}

.week-scroll {
  overflow-x: auto;
  background: #fff;
  border-top: 1px solid var(--cal-line);
  border-bottom: 1px solid var(--cal-line);
}

.week-grid {
  border-collapse: collapse;
  min-width: 560px;
  width: 100%;
}

.week-grid th,
.week-grid td {
  border-bottom: 1px solid var(--cal-line);
  padding: 7px 4px;
  text-align: center;
  font-size: 12px;
}

.week-grid .emp-col {
  position: sticky;
  left: 0;
  background: #fff;
  text-align: left;
  min-width: 86px;
  z-index: 1;
  border-right: 1px solid var(--cal-line);
}

.week-grid .day-head {
  padding: 8px 4px 6px;
}

.dh-dow {
  font-size: 11px;
  color: var(--cal-sub);
  font-weight: 400;
}

.dh-dow.is-today {
  color: var(--cal-red);
}

.dh-num {
  margin-top: 3px;
  font-size: 15px;
  color: var(--cal-text);
  font-weight: 500;
}

.dh-cnt {
  margin-top: 2px;
  font-size: 9px;
  color: var(--cal-sub);
}

.week-grid .day-cell.is-today {
  background: #fff5f5;
}

.emp-name {
  font-size: 12px;
  color: var(--cal-text);
  font-weight: 600;
  white-space: nowrap;
}

.emp-no {
  font-size: 10px;
  color: var(--cal-sub);
}

.cell-shift {
  color: var(--cal-text);
  font-weight: 700;
  font-size: 12px;
}

.cell-time {
  color: var(--cal-sub);
  font-size: 10px;
  white-space: nowrap;
}

.cell-rest {
  color: var(--cal-sub);
}

.cell-none {
  color: #d1d1d6;
}

/* ===== 月视图：日历样式 ===== */
.cal-grid {
  margin: 0 12px;
  background: #fff;
  border-radius: 14px;
  padding: 10px 6px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.cal-weekdays {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  text-align: center;
  font-size: 12px;
  color: var(--cal-sub);
  padding: 2px 0 6px;
}

.cal-week {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
}

.cal-cell {
  min-height: 54px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  padding-top: 4px;
  border-radius: 8px;
}

.cal-num {
  font-size: 14px;
  color: var(--cal-text);
}

.cal-cell.is-out .cal-num {
  color: #c7c7cc;
}

.cal-cell.is-today {
  background: #fff5f5;
}

.cal-count {
  margin-top: 2px;
  font-size: 9px;
  line-height: 1.3;
}

.cal-work {
  color: var(--cal-blue);
}

.cal-rest {
  color: var(--cal-sub);
}

.cal-nodata {
  margin-top: 4px;
  color: #e3e3e8;
}

/* ===== 日视图 ===== */
.day-range {
  color: var(--cal-text);
}

.day-list {
  background: #fff;
  border-top: 1px solid var(--cal-line);
  border-bottom: 1px solid var(--cal-line);
  padding: 4px 0;
}

.day-item {
  padding: 10px 20px;
  border-bottom: 1px solid var(--cal-line);
}

.day-item:last-child {
  border-bottom: none;
}

.di-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.di-name {
  font-size: 15px;
  font-weight: 700;
  color: var(--cal-text);
}

.di-no {
  font-size: 11px;
  color: var(--cal-sub);
}

.di-shift {
  margin-left: auto;
  font-size: 12px;
  color: var(--cal-blue);
  font-weight: 700;
}

.di-time {
  margin-top: 4px;
  font-size: 13px;
  color: var(--cal-blue);
  font-weight: 500;
}

.di-break {
  margin-top: 2px;
  font-size: 12px;
  color: var(--cal-orange);
}

.day-rest {
  background: #fff;
  margin-top: 10px;
  border-top: 1px solid var(--cal-line);
  border-bottom: 1px solid var(--cal-line);
  padding: 10px 20px;
}

.dr-title {
  font-size: 12px;
  color: var(--cal-sub);
}

.dr-names {
  margin-top: 4px;
  font-size: 13px;
  color: var(--cal-text);
  line-height: 1.8;
}
</style>
