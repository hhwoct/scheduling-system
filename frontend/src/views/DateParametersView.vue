<template>
  <div>
    <el-card>
      <template #header>
        <div class="header-row">
          <span>日期参数（节假日/工作日配置）</span>
          <div class="header-actions">
            <el-button-group>
              <el-button :disabled="loading" @click="shiftMonth(-1)">上月</el-button>
              <el-date-picker
                v-model="month"
                type="month"
                placeholder="选择月份"
                value-format="YYYY-MM"
                :clearable="false"
                style="width: 140px"
                :disabled="loading"
                @change="load"
              />
              <el-button :disabled="loading" @click="shiftMonth(1)">下月</el-button>
            </el-button-group>
            <span style="margin: 0 12px" />
            <el-input-number v-model="generateMonths" :min="1" :max="12" size="small" style="width: 90px" />
            <el-button :loading="generating" :disabled="loading" @click="generate">按规则补全未来 N 个月</el-button>
            <el-button type="primary" :loading="saving" :disabled="loading || !hasChanges" @click="save">
              保存本月（{{ changedCount }}）
            </el-button>
          </div>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="点击日期可切换类型：平日 → 周末 → 节假日 → 平日；法定节假日自动带「法」标记，调休补班日请把周末点回「平日」。虚线格子 = 数据库中尚未配置（当前为按规则的建议值）。"
        style="margin-bottom: 12px"
      />

      <div v-loading="loading" class="calendar">
        <div v-for="w in ['周一', '周二', '周三', '周四', '周五', '周六', '周日']" :key="w" class="cell weekday-header">
          {{ w }}
        </div>
        <template v-for="(cell, i) in cells" :key="i">
          <div v-if="!cell" class="cell empty" />
          <div
            v-else
            class="cell day"
            :class="{
              weekend: cell.dayType === 'WEEKEND',
              holiday: cell.dayType === 'HOLIDAY',
              unconfigured: !cell.isConfigured,
              dirty: cell.dirty,
              today: cell.isToday
            }"
            @click="toggle(cell)"
          >
            <span v-if="cell.isLegalHoliday" class="legal-flag">法</span>
            <div class="day-number">{{ cell.day }}</div>
            <div class="day-type">{{ typeLabel(cell.dayType) }}</div>
          </div>
        </template>
      </div>

      <div class="legend">
        <span><span class="legend-box workday" />平日</span>
        <span><span class="legend-box weekend" />周末（周五/周六）</span>
        <span><span class="legend-box holiday" />节假日</span>
        <span><span class="legend-box legal-flag-box">法</span>法定节假日</span>
        <span><span class="legend-box unconfigured" />未配置（建议值）</span>
        <span><span class="legend-box dirty" />已修改未保存</span>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { getDateParameters, saveDateParameters, generateDateParameters } from '../api/dateParameters'

const loading = ref(false)
const saving = ref(false)
const generating = ref(false)
const month = ref('')
const generateMonths = ref(3)

// 原始后端数据（workDate → item），用于 dirty 判定
const originalMap = ref({})
// 当前视图格子：null = 占位，否则为可编辑日
const cells = ref([])

const WEEKDAYS = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']

function typeLabel(type) {
  return { WORKDAY: '平日', WEEKEND: '周末', HOLIDAY: '节假日' }[type] || type
}

function formatMonth(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  return `${y}-${m}`
}

function shiftMonth(delta) {
  const [y, m] = month.value.split('-').map(Number)
  const d = new Date(y, m - 1 + delta, 1)
  month.value = formatMonth(d)
  load()
}

async function load() {
  if (!month.value) return
  loading.value = true
  try {
    const data = await getDateParameters(month.value)
    const items = data.items || []
    originalMap.value = Object.fromEntries(items.map(x => [x.workDate, x]))

    const [y, m] = month.value.split('-').map(Number)
    const first = new Date(y, m - 1, 1)
    const dayCount = new Date(y, m, 0).getDate()
    const offset = (first.getDay() + 6) % 7 // 周一开始：周日=0 → 6
    const todayStr = formatMonth(new Date()) + '-' + String(new Date().getDate()).padStart(2, '0')

    const list = []
    for (let i = 0; i < offset; i++) list.push(null)
    for (let d = 1; d <= dayCount; d++) {
      const workDate = `${month.value}-${String(d).padStart(2, '0')}`
      const base = items.find(x => x.workDate === workDate)
      list.push({
        workDate,
        day: d,
        isToday: workDate === todayStr,
        dayType: base ? base.dayType : 'WORKDAY',
        isLegalHoliday: base ? base.isLegalHoliday : 0,
        isHolidayEve: base ? base.isHolidayEve : 0,
        isConfigured: base ? base.isConfigured : false,
        original: base ? JSON.stringify({ dayType: base.dayType, isLegalHoliday: base.isLegalHoliday, isHolidayEve: base.isHolidayEve }) : 'null'
      })
    }
    cells.value = list
  } finally {
    loading.value = false
  }
}

function currentSnapshot(cell) {
  return JSON.stringify({ dayType: cell.dayType, isLegalHoliday: cell.isLegalHoliday, isHolidayEve: cell.isHolidayEve })
}

function isDirty(cell) {
  return cell.original !== currentSnapshot(cell)
}

function toggle(cell) {
  const order = { WORKDAY: 'WEEKEND', WEEKEND: 'HOLIDAY', HOLIDAY: 'WORKDAY' }
  cell.dayType = order[cell.dayType] || 'WORKDAY'
  // 切到节假日自动视为法定节假日；切走清除
  cell.isLegalHoliday = cell.dayType === 'HOLIDAY' ? 1 : 0
  cell.dirty = isDirty(cell)
}

const changedCount = computed(() => cells.value.filter(c => c && c.dirty).length)
const hasChanges = computed(() => changedCount.value > 0)

async function save() {
  const dirty = cells.value.filter(c => c && c.dirty)
  if (dirty.length === 0) return
  saving.value = true
  try {
    const items = dirty.map(c => ({
      workDate: c.workDate,
      dayType: c.dayType,
      isLegalHoliday: c.isLegalHoliday,
      isHolidayEve: c.isHolidayEve
    }))
    const result = await saveDateParameters(items)
    ElMessage.success(`已保存：新增 ${result.newCount} 条、更新 ${result.updatedCount} 条`)
    await load()
  } finally {
    saving.value = false
  }
}

async function generate() {
  generating.value = true
  try {
    const result = await generateDateParameters(generateMonths.value)
    ElMessage.success(`已按规则补全 ${generateMonths.value} 个月（${result.startDate} ~ ${result.endDate}），新增 ${result.inserted} 条；已配置日期（含节假日）保留不变`)
    await load()
  } finally {
    generating.value = false
  }
}

onMounted(() => {
  if (!month.value) month.value = formatMonth(new Date())
  load()
})
</script>

<style scoped>
.header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
}
.header-actions {
  display: flex;
  align-items: center;
}
.calendar {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: 4px;
}
.cell {
  min-height: 64px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 4px;
  position: relative;
  text-align: center;
  padding: 4px;
  box-sizing: border-box;
}
.weekday-header {
  min-height: 28px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-regular);
  font-weight: 600;
  line-height: 28px;
  border: none;
}
.cell.empty {
  border: none;
  background: transparent;
}
.cell.day {
  cursor: pointer;
  background: var(--el-bg-color);
  transition: background 0.15s;
}
.cell.day:hover {
  outline: 2px solid var(--el-color-primary);
}
.cell.day.weekend {
  background: var(--el-color-primary-light-9);
  border-color: var(--el-color-primary-light-5);
}
.cell.day.holiday {
  background: var(--el-color-danger-light-9);
  border-color: var(--el-color-danger-light-7);
}
.cell.day.unconfigured {
  border-style: dashed;
  opacity: 0.75;
}
.cell.day.dirty {
  outline: 2px solid var(--el-color-warning);
}
.cell.day.today .day-number {
  color: var(--el-color-primary);
  font-weight: 700;
}
.legal-flag {
  position: absolute;
  top: 2px;
  right: 4px;
  background: var(--el-color-danger);
  color: var(--el-color-white);
  font-size: 10px;
  line-height: 14px;
  border-radius: 3px;
  padding: 0 3px;
}
.day-number {
  font-size: 15px;
  margin-top: 6px;
}
.day-type {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-top: 4px;
}
.legend {
  margin-top: 12px;
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--el-text-color-regular);
}
.legend-box {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 3px;
  vertical-align: -2px;
  margin-right: 4px;
  border: 1px solid var(--el-border-color);
}
.legend-box.workday { background: var(--el-bg-color); }
.legend-box.weekend { background: var(--el-color-primary-light-9); }
.legend-box.holiday { background: var(--el-color-danger-light-9); }
.legend-box.legal-flag-box {
  background: var(--el-color-danger);
  color: var(--el-color-white);
  font-size: 10px;
  text-align: center;
  line-height: 14px;
  border: none;
}
.legend-box.unconfigured { border-style: dashed; }
.legend-box.dirty { outline: 2px solid var(--el-color-warning); border: none; }
</style>
