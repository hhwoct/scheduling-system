<template>
  <div>
    <el-card style="margin-bottom: 16px">
      <template #header>班次甘特图（13:00 ~ 次日 06:00）</template>
      <div class="gantt-wrap">
        <!-- 时间轴刻度 -->
        <div class="gantt-axis">
          <div class="gantt-label"></div>
          <div class="gantt-bar-area">
            <div
              v-for="(t, i) in ganttHours"
              :key="t"
              class="axis-tick"
              :style="{ left: (i * 100 / (ganttHours.length - 1)) + '%' }"
            >
              {{ t }}
            </div>
          </div>
        </div>
        <!-- 每个班次一行 -->
        <div v-for="shift in list" :key="shift.code" class="gantt-row">
          <div class="gantt-label">{{ shift.name }}</div>
          <div class="gantt-bar-area">
            <div
              class="gantt-bar"
              :style="barStyle(shift)"
            >
              {{ shift.name }}
            </div>
          </div>
        </div>
        <div class="gantt-legend">
          <span v-for="s in list" :key="s.code" class="legend-item">
            <span class="legend-box" :style="{ background: shiftColor(s.code) }"></span> {{ s.name }}
          </span>
        </div>
      </div>
    </el-card>

    <!-- 高峰禁休时段设置（原独立页面迁入：泳道图下方、班次表格上方） -->
    <PeakHoursSection />

    <el-card>
      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="code" label="班次" width="80" />
        <el-table-column prop="name" label="名称" width="180" />
        <el-table-column label="开始时间" width="120">
          <template #default="{ row }">{{ formatTime(row.startTime) }}</template>
        </el-table-column>
        <el-table-column label="结束时间" width="120">
          <template #default="{ row }">{{ formatTime(row.endTime) }}</template>
        </el-table-column>
        <el-table-column label="跨天" width="80">
          <template #default="{ row }">
            <el-tag :type="isCrossDayShift(row.startTime, row.endTime) ? 'warning' : 'info'">{{ isCrossDayShift(row.startTime, row.endTime) ? '是' : '否' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="priority" label="优先级" width="80" />
        <el-table-column prop="coveredWorkstations" label="覆盖工作站">
          <template #default="{ row }">
            <el-tag v-for="ws in (Array.isArray(row.coveredWorkstations) ? row.coveredWorkstations : String(row.coveredWorkstations || '').split(',').filter(Boolean))" :key="ws" size="small" style="margin-right: 4px">{{ ws }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" title="编辑班次" width="500px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="开始时间" required>
          <el-time-select v-model="form.startTime" start="00:00" step="00:30" end="23:30" style="width: 100%" />
        </el-form-item>
        <el-form-item label="结束时间" required>
          <el-time-select v-model="form.endTime" start="00:00" step="00:30" end="23:30" style="width: 100%" />
        </el-form-item>
        <el-form-item label="跨天">
          <el-switch v-model="form.isCrossDay" :active-value="1" :inactive-value="0" active-text="是" inactive-text="否" />
        </el-form-item>
        <el-form-item label="优先级">
          <!-- S10 店长班优先级由算法 ShiftPriorityMap 固定（先于行政班分配，保证副手顶班），
               界面修改不生效，故禁用 -->
          <el-input-number v-model="form.priority" :min="1" :max="100" :disabled="form.code === 'S10'" />
          <div v-if="form.code === 'S10'" class="form-tip">店长班优先级由算法固定，不可修改</div>
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="form.status" :active-value="1" :inactive-value="0" active-text="启用" inactive-text="停用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getShiftTemplates, updateShiftTemplate } from '../api/shiftTemplates'
import PeakHoursSection from '../components/PeakHoursSection.vue'

const loading = ref(false)
const list = ref([])
const dialogVisible = ref(false)
const saving = ref(false)
const editingId = ref(0)
const form = reactive({
  name: '',
  startTime: '13:00',
  endTime: '22:00',
  isCrossDay: 0,
  priority: 5,
  status: 1
})

function formatTime(t) {
  if (!t) return '--'
  return String(t).substring(0, 5)
}

async function loadData() {
  loading.value = true
  try {
    list.value = await getShiftTemplates()
  } finally {
    loading.value = false
  }
}

function openEdit(row) {
  editingId.value = row.id
  form.name = row.name
  form.startTime = row.startTime ? String(row.startTime).substring(0, 5) : '00:00'
  form.endTime = row.endTime ? String(row.endTime).substring(0, 5) : '00:00'
  form.isCrossDay = row.isCrossDay
  form.priority = row.priority
  form.status = row.status
  dialogVisible.value = true
}

async function handleSave() {
  // 跨天开关与起止时间自动同步（与甘特图/表格的 isCrossDayShift 判定一致）
  form.isCrossDay = isCrossDayShift(form.startTime, form.endTime) ? 1 : 0
  saving.value = true
  try {
    await updateShiftTemplate(editingId.value, {
      name: form.name,
      startTime: form.startTime + ':00',
      endTime: form.endTime + ':00',
      isCrossDay: form.isCrossDay,
      priority: form.priority,
      status: form.status
    })
    ElMessage.success('保存成功')
    dialogVisible.value = false
    loadData()
  } finally {
    saving.value = false
  }
}

// 甘特图辅助 — 连续横条，时间范围 13:00 ~ 次日 06:00
const GANTT_START = 13 * 60  // 13:00 in minutes
const GANTT_END = (24 + 6) * 60  // 次日 06:00 = 30:00 in minutes
const GANTT_DURATION = GANTT_END - GANTT_START  // 17h = 1020 min

const COLORS = ['#409eff','#67c23a','#e6a23c','#f56c6c','#909399','#b37feb','#ff85c0','#36cfc9']

// 时间轴刻度：13:00 到 06:00（次日）
const ganttHours = (() => {
  const arr = []
  for (let h = 13; ; h++) {
    const real = h % 24
    arr.push(`${String(real).padStart(2,'0')}:00`)
    if (h === 30) break // 次日 06:00
  }
  return arr
})()

function shiftColor(code) {
  const idx = (list.value || []).findIndex(s => s.code === code)
  return COLORS[idx % COLORS.length] || '#ccc'
}

function toMinutes(t) {
  if (!t) return 0
  const parts = String(t).split(':')
  return parseInt(parts[0]) * 60 + parseInt(parts[1])
}

// 统一跨天判定：结束时间严格早于开始时间；start==end 视为 0 时长而非跨天
function isCrossDayShift(startTime, endTime) {
  if (!startTime || !endTime) return false
  return toMinutes(endTime) < toMinutes(startTime)
}

// 甘特图横条：统一用 isCrossDayShift 判定跨天，不依赖 isCrossDay 标志
function barStyle(shift) {
  let startMin = toMinutes(shift.startTime)
  let endMin = toMinutes(shift.endTime)

  if (isCrossDayShift(shift.startTime, shift.endTime)) {
    endMin += 24 * 60
  }

  // 裁剪到甘特图范围（13:00 ~ 次日06:00）
  const clampedStart = Math.max(startMin, GANTT_START)
  const clampedEnd = Math.min(endMin, GANTT_END)

  if (clampedStart >= clampedEnd) return { display: 'none' }

  const leftPct = ((clampedStart - GANTT_START) / GANTT_DURATION) * 100
  const widthPct = ((clampedEnd - clampedStart) / GANTT_DURATION) * 100

  return {
    background: shiftColor(shift.code),
    left: leftPct + '%',
    width: widthPct + '%'
  }
}

onMounted(loadData)
</script>

<style scoped>
.form-tip {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.4;
  margin-top: 2px;
}
.gantt-wrap {
  overflow-x: auto;
  padding-bottom: 8px;
}
.gantt-axis {
  display: flex;
  align-items: flex-end;
  margin-bottom: 0;
}
.gantt-axis .gantt-label {
  height: 24px;
}
.gantt-bar-area {
  flex: 1;
  position: relative;
  height: 24px;
  display: flex;
}
.axis-tick {
  position: absolute;
  transform: translateX(-50%);
  font-size: 9px;
  text-align: center;
  color: var(--el-text-color-secondary);
  border-left: 1px solid var(--el-border-color-light);
  line-height: 20px;
  flex-shrink: 0;
  white-space: nowrap;
}
.gantt-bar-area .axis-tick:first-child {
  border-left: none;
}
.gantt-row {
  display: flex;
  align-items: center;
  margin-bottom: 6px;
}
.gantt-label {
  width: 100px;
  min-width: 100px;
  font-size: 12px;
  padding-right: 8px;
  text-align: right;
  color: var(--el-text-color-regular);
  line-height: 28px;
}
.gantt-row .gantt-bar-area {
  flex: 1;
  position: relative;
  height: 28px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
  overflow: hidden;
}
.gantt-bar {
  position: absolute;
  top: 4px;
  height: 20px;
  border-radius: 10px;
  color: var(--el-color-white);
  font-size: 11px;
  text-align: center;
  line-height: 20px;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  padding: 0 4px;
  box-sizing: border-box;
}
.gantt-legend {
  margin-top: 16px;
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
}
.legend-item {
  font-size: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.legend-box {
  width: 20px;
  height: 14px;
  border-radius: 3px;
  display: inline-block;
}
</style>
