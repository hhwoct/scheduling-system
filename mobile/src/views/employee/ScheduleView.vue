<template>
  <div class="schedule-page">
    <van-nav-bar title="我的班表">
      <template #right>
        <van-icon name="replay" size="18" @click="refresh" />
      </template>
    </van-nav-bar>

    <!-- 今日卡片（苹果日历日视图风格：白卡 + 红色今天） -->
    <div class="today-card" @click="openDayDetail(todayStr)">
      <template v-if="todayDay && todayDay.isRestDay !== 1">
        <div class="tc-head">
          <span class="tc-date"><i class="tc-red-dot"></i>{{ todayLabel }}</span>
          <span class="tc-shift" v-if="todayDay.shiftCode">{{ todayDay.shiftCode }}</span>
        </div>
        <div class="tc-time">{{ fmtTime(todayDay.startTime) }} - {{ fmtTime(todayDay.endTime) }}</div>
        <div class="tc-ws" v-if="wsList(todayDay).length">
          <span v-for="ws in wsList(todayDay)" :key="ws" class="tc-ws-item"><i class="ws-dot"></i>{{ ws }}</span>
        </div>
        <div class="tc-break" v-if="todayDay.breakStartTime">
          班中休息 {{ fmtTime(todayDay.breakStartTime) }}-{{ fmtTime(todayDay.breakEndTime) }}
          <span v-if="todayDay.coverEmployeeName" class="tc-cover-ok">由 {{ todayDay.coverEmployeeName }} 顶班</span>
        </div>
      </template>
      <template v-else>
        <div class="tc-head">
          <span class="tc-date"><i class="tc-red-dot"></i>{{ todayLabel }}</span>
        </div>
        <div class="tc-rest">今日休息</div>
        <div class="tc-next" v-if="nextShift">
          下一个班次：{{ nextShift.dateLabel }} {{ fmtTime(nextShift.startTime) }}-{{ fmtTime(nextShift.endTime) }}<template v-if="nextShift.shiftCode"> · {{ nextShift.shiftCode }}</template>
        </div>
        <div class="tc-next" v-else>近期暂无排班</div>
      </template>
    </div>

    <!-- iOS 风格分段切换器 -->
    <div class="segmented">
      <span class="seg" :class="{ 'seg-active': viewTab === 'week' }" @click="viewTab = 'week'">周</span>
      <span class="seg" :class="{ 'seg-active': viewTab === 'month' }" @click="viewTab = 'month'">月</span>
    </div>

    <!-- 周视图 -->
    <div v-show="viewTab === 'week'" class="view-pane">
      <div class="view-toolbar">
        <van-icon name="arrow-left" @click="moveWeek(-1)" />
        <span class="view-range">{{ weekRangeLabel }}</span>
        <van-icon name="arrow" @click="moveWeek(1)" />
      </div>

      <div class="week-list">
        <div
          v-for="d in weekDays"
          :key="d.date"
          class="week-row"
          :class="{ 'is-today': d.date === todayStr }"
          @click="openDayDetail(d.date)"
        >
          <div class="wr-date">
            <div class="wr-dow" :class="{ 'is-today': d.date === todayStr }">{{ d.dow }}</div>
            <div class="wr-num" :class="{ 'today-circle': d.date === todayStr }">{{ d.num }}</div>
          </div>
          <div class="wr-main">
            <template v-if="d.day && d.day.isRestDay !== 1">
              <div class="wr-shift">{{ d.day.shiftCode || '上班' }}<span class="wr-time">{{ fmtTime(d.day.startTime) }}-{{ fmtTime(d.day.endTime) }}</span></div>
              <div class="wr-sub" v-if="wsList(d.day).length">{{ wsList(d.day).join('、') }}</div>
            </template>
            <div v-else class="wr-rest">休息</div>
          </div>
          <van-icon name="arrow" class="wr-arrow" />
        </div>
        <van-empty v-if="!weekDays.some((d) => d.day)" description="本周暂无排班数据" image-size="72" />
      </div>
    </div>

    <!-- 月视图（苹果日历网格） -->
    <div v-show="viewTab === 'month'" class="view-pane">
      <div class="view-toolbar">
        <van-icon name="arrow-left" @click="moveMonth(-1)" />
        <span class="view-range">{{ monthCursorLabel }}</span>
        <van-icon name="arrow" @click="moveMonth(1)" />
      </div>

      <div class="cal-grid">
        <div class="cal-weekdays">
          <span v-for="w in ['一', '二', '三', '四', '五', '六', '日']" :key="w">{{ w }}</span>
        </div>
        <div class="cal-week" v-for="(week, wi) in monthWeeks" :key="wi">
          <div
            v-for="cell in week"
            :key="cell.date"
            class="cal-cell"
            :class="{ 'is-today': cell.isToday, 'is-out': !cell.inMonth }"
            @click="openDayDetail(cell.date)"
          >
            <span class="cal-num" :class="{ 'today-circle': cell.isToday }">{{ cell.num }}</span>
            <span v-if="cell.day" class="cal-dot" :class="cell.day.isRestDay === 1 ? 'dot-rest' : 'dot-work'"></span>
            <span v-else-if="!cell.inMonth" class="cal-dot dot-empty"></span>
          </div>
        </div>
        <div class="cal-legend">
          <span class="legend-item"><i class="dot-work"></i>上班</span>
          <span class="legend-item"><i class="dot-rest"></i>休息</span>
        </div>
      </div>
    </div>

    <div class="covers-entry" v-if="covers.length" @click="showCovers = true">
      <span class="ce-title">我顶岗的记录</span>
      <span class="ce-value">{{ covers.length }} 条</span>
      <van-icon name="arrow" class="ce-arrow" />
    </div>

    <van-loading v-if="loading" class="page-loading" size="24" vertical>加载中…</van-loading>

    <!-- 日详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop">
        <div class="detail-head">
          <span>{{ detailDateLabel }}</span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <div v-if="detailDay" class="detail-body">
          <div class="db-status">
            <span class="db-dot" :class="detailDay.isRestDay === 1 ? 'dot-rest' : 'dot-work'"></span>
            <span>{{ detailDay.isRestDay === 1 ? '休息' : '上班' }}</span>
            <span v-if="detailDay.isRestDay !== 1 && detailDay.shiftCode" class="db-shift">{{ detailDay.shiftCode }}</span>
          </div>
          <template v-if="detailDay.isRestDay !== 1">
            <div class="db-row"><span class="db-label">时间</span>{{ fmtTime(detailDay.startTime) }} - {{ fmtTime(detailDay.endTime) }}</div>
            <div class="db-row"><span class="db-label">工时</span>{{ formatHours(detailDay.workHours) }} 小时</div>
            <div class="db-row"><span class="db-label">岗位</span>{{ wsList(detailDay).join('、') || '--' }}</div>
            <div class="db-row" v-if="detailDay.breakStartTime"><span class="db-label">班中休息</span>{{ fmtTime(detailDay.breakStartTime) }}-{{ fmtTime(detailDay.breakEndTime) }}</div>
            <div class="db-row" v-if="detailDay.breakStartTime && detailDay.coverEmployeeName"><span class="db-label">休息顶岗</span>{{ detailDay.coverEmployeeName }}</div>
          </template>
        </div>
        <van-empty v-else description="当天无排班数据" image-size="72" />
      </div>
    </van-popup>

    <!-- 顶岗记录弹层 -->
    <van-popup v-model:show="showCovers" position="bottom" round>
      <div class="detail-pop">
        <div class="detail-head">
          <span>我顶岗的记录</span>
          <van-icon name="cross" @click="showCovers = false" />
        </div>
        <div class="cover-list">
          <div class="cover-item" v-for="(c, i) in covers" :key="i">
            <div class="ci-date">{{ c.workDate }} {{ fmtTime(c.breakStartTime) }}-{{ fmtTime(c.breakEndTime) }}</div>
            <div class="ci-sub">岗位：{{ c.workstationName || '--' }} · 替：{{ c.forEmployeeName || '--' }}</div>
          </div>
        </div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { showToast } from 'vant'
import { getMySchedule } from '../../api/employee'

const viewTab = ref('week')
const loading = ref(false)
const monthCache = new Map()
const dayMap = ref(new Map())
const covers = ref([])
const weekStart = ref(mondayOf(dayjs()))
const monthCursor = ref(dayjs().format('YYYY-MM'))
const todayStr = dayjs().format('YYYY-MM-DD')

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

function formatHours(h) {
  const n = Number(h)
  return Number.isFinite(n) ? n.toFixed(1) : '0.0'
}

function wsList(day) {
  if (!day || !day.workstationNames) return []
  return String(day.workstationNames).split('、').filter(Boolean)
}

function applyData(data) {
  ;(data?.plans || []).forEach((plan) => {
    ;(plan.days || []).forEach((d) => {
      dayMap.value.set(String(d.workDate), { ...d, planName: plan.planName })
    })
  })
  if (Array.isArray(data?.covers)) covers.value = data.covers
}

async function fetchMonth(month) {
  if (monthCache.has(month)) return monthCache.get(month)
  loading.value = true
  try {
    const data = await getMySchedule(month)
    monthCache.set(month, data)
    applyData(data)
    return data
  } catch (e) {
    return null
  } finally {
    loading.value = false
  }
}

async function ensureWeek() {
  const m1 = dstr(weekStart.value)
  const m2 = dstr(weekStart.value.add(6, 'day'))
  await Promise.all([fetchMonth(m1), fetchMonth(m2)])
}

const weekDays = computed(() =>
  Array.from({ length: 7 }, (_, i) => {
    const d = weekStart.value.add(i, 'day')
    const date = dstr(d)
    const dow = d.day() === 0 ? 6 : d.day() - 1
    return {
      date,
      dow: '周' + '一二三四五六日'[dow],
      num: d.date(),
      day: dayMap.value.get(date) || null
    }
  })
)

const weekRangeLabel = computed(() => {
  const s = weekStart.value
  const e = s.add(6, 'day')
  return s.month() + 1 + '月' + s.date() + '日 - ' + (e.month() + 1) + '月' + e.date() + '日'
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
      row.push({
        date,
        num: d.date(),
        inMonth: d.month() === first.month(),
        isToday: date === todayStr,
        day: dayMap.value.get(date) || null
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

const todayDay = computed(() => dayMap.value.get(todayStr) || null)

const todayLabel = computed(() => {
  const d = dayjs()
  const dow = d.day() === 0 ? 6 : d.day() - 1
  return d.month() + 1 + '月' + d.date() + '日 周' + '一二三四五六日'[dow]
})

const nextShift = computed(() => {
  for (let i = 1; i <= 31; i++) {
    const d = dayjs().add(i, 'day')
    const day = dayMap.value.get(dstr(d))
    if (day && day.isRestDay !== 1) {
      return {
        dateLabel: d.month() + 1 + '月' + d.date() + '日',
        shiftCode: day.shiftCode,
        startTime: day.startTime,
        endTime: day.endTime
      }
    }
  }
  return null
})

function moveWeek(delta) {
  weekStart.value = weekStart.value.add(delta * 7, 'day')
  ensureWeek()
}

async function moveMonth(delta) {
  monthCursor.value = dayjs(monthCursor.value + '-01')
    .add(delta, 'month')
    .format('YYYY-MM')
  await fetchMonth(monthCursor.value)
}

const showDetail = ref(false)
const detailDate = ref('')

const detailDay = computed(() => dayMap.value.get(detailDate.value) || null)

const detailDateLabel = computed(() => {
  if (!detailDate.value) return ''
  const d = dayjs(detailDate.value)
  const dow = d.day() === 0 ? 6 : d.day() - 1
  return d.month() + 1 + '月' + d.date() + '日 周' + '一二三四五六日'[dow]
})

function openDayDetail(date) {
  detailDate.value = date
  showDetail.value = true
}

const showCovers = ref(false)

async function refresh() {
  monthCache.clear()
  dayMap.value = new Map()
  covers.value = []
  await ensureWeek()
  showToast('已刷新')
}

onMounted(() => {
  ensureWeek()
})
</script>

<style scoped>
.schedule-page {
  min-height: 100vh;
  background: #f2f2f7;
  --cal-red: #ff3b30;
  --cal-blue: #007aff;
  --cal-orange: #ff9500;
  --cal-text: #1c1c1e;
  --cal-sub: #8e8e93;
  --cal-line: #e5e5ea;
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

/* 今日卡片：白卡 + 红色今天（苹果日历日视图） */
.today-card {
  margin: 12px 16px 0;
  padding: 14px 16px;
  border-radius: 14px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}

.tc-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.tc-date {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--cal-red);
}

.tc-red-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--cal-red);
  display: inline-block;
}

.tc-shift {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 27px;
  height: 27px;
  padding: 0 4px;
  font-size: 11px;
  color: #fff;
  background: var(--cal-blue);
  border-radius: 50%;
  font-weight: 700;
  box-sizing: border-box;
}

.tc-time {
  margin-top: 8px;
  font-size: 28px;
  font-weight: 700;
  color: var(--cal-text);
  letter-spacing: 0.5px;
}

.tc-rest {
  margin-top: 8px;
  font-size: 24px;
  font-weight: 700;
  color: var(--cal-text);
}

.tc-next {
  margin-top: 6px;
  font-size: 13px;
  color: var(--cal-sub);
}

.tc-ws {
  margin-top: 8px;
}

.tc-ws-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-right: 10px;
  font-size: 12px;
  color: var(--cal-text);
}

.ws-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--cal-blue);
  display: inline-block;
}

.tc-break {
  margin-top: 10px;
  font-size: 12px;
  color: var(--cal-orange);
}

.tc-cover-ok {
  margin-left: 6px;
  color: var(--cal-sub);
}

.tc-cover-warn {
  margin-left: 6px;
  color: var(--cal-orange);
}

/* 分段切换器 */
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
}

.seg-active {
  background: #fff;
  font-weight: 600;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
}

.view-pane {
  padding-bottom: 8px;
}

.view-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 20px 8px;
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
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 50%;
  background: var(--cal-red);
  color: #fff !important;
  font-weight: 600;
  font-size: 14px;
}

/* 周列表 */
.week-list {
  background: #fff;
  border-top: 1px solid var(--cal-line);
  border-bottom: 1px solid var(--cal-line);
}

.week-row {
  display: flex;
  align-items: center;
  padding: 11px 16px;
  border-bottom: 1px solid var(--cal-line);
}

.week-row:last-child {
  border-bottom: none;
}

.week-row.is-today {
  background: #fff5f5;
}

.wr-date {
  width: 58px;
  text-align: center;
}

.wr-dow {
  font-size: 11px;
  color: var(--cal-sub);
}

.wr-dow.is-today {
  color: var(--cal-red);
  font-weight: 600;
}

.wr-num {
  margin-top: 2px;
  font-size: 17px;
  font-weight: 500;
  color: var(--cal-text);
}

.wr-main {
  flex: 1;
  margin-left: 12px;
}

.wr-shift {
  font-size: 15px;
  font-weight: 700;
  color: var(--cal-text);
}

.wr-time {
  margin-left: 8px;
  font-size: 13px;
  font-weight: 400;
  color: var(--cal-sub);
}

.wr-sub {
  margin-top: 2px;
  font-size: 12px;
  color: var(--cal-blue);
}

.wr-rest {
  font-size: 14px;
  color: var(--cal-sub);
}

.wr-arrow {
  color: #c7c7cc;
}

/* 月历网格 */
.cal-grid {
  margin: 0 10px;
  background: #fff;
  border-radius: 14px;
  padding: 10px 6px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
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
  height: 46px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
}

.cal-num {
  font-size: 15px;
  color: var(--cal-text);
}

.cal-cell.is-out .cal-num {
  color: #c7c7cc;
}

.cal-cell.is-today {
  background: #fff5f5;
}

.cal-dot {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  margin-top: 3px;
}

.dot-work {
  background: var(--cal-blue);
}

.dot-rest {
  background: var(--cal-red);
}

.dot-empty {
  background: transparent;
}

.cal-legend {
  display: flex;
  justify-content: center;
  gap: 16px;
  padding: 6px 0 2px;
  font-size: 11px;
  color: var(--cal-sub);
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.legend-item i {
  display: inline-block;
  width: 6px;
  height: 6px;
  border-radius: 50%;
}

/* 顶岗入口 */
.covers-entry {
  display: flex;
  align-items: center;
  margin: 12px 16px;
  padding: 12px 16px;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}

.ce-title {
  font-size: 14px;
  color: var(--cal-text);
  font-weight: 600;
}

.ce-value {
  margin-left: auto;
  font-size: 12px;
  color: var(--cal-sub);
}

.ce-arrow {
  margin-left: 6px;
  color: #c7c7cc;
}

/* 弹层 */
.detail-pop {
  padding: 12px 0 calc(20px + env(safe-area-inset-bottom));
  max-height: 75vh;
  overflow-y: auto;
}

.detail-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 20px 12px;
  font-size: 16px;
  font-weight: 700;
  color: var(--cal-text);
}

.detail-body {
  padding: 0 20px;
}

.db-status {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 15px;
  font-weight: 700;
  color: var(--cal-text);
  padding-bottom: 10px;
}

.db-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  display: inline-block;
}

.db-shift {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 27px;
  height: 27px;
  padding: 0 4px;
  font-size: 11px;
  color: #fff;
  background: var(--cal-blue);
  border-radius: 50%;
  font-weight: 700;
  box-sizing: border-box;
}

.db-row {
  display: flex;
  align-items: center;
  padding: 9px 0;
  border-bottom: 1px solid var(--cal-line);
  font-size: 14px;
  color: var(--cal-text);
}

.db-label {
  width: 76px;
  font-size: 12px;
  color: var(--cal-sub);
}

.cover-list {
  padding: 0 20px;
}

.cover-item {
  padding: 10px 0;
  border-bottom: 1px solid var(--cal-line);
}

.ci-date {
  font-size: 14px;
  font-weight: 600;
  color: var(--cal-text);
}

.ci-sub {
  margin-top: 3px;
  font-size: 12px;
  color: var(--cal-sub);
}
</style>
