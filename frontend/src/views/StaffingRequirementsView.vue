<template>
  <div>
    <el-card>
      <template #header>
        <div class="card-header">
          <span>人数需求（30 分钟粒度）</span>
          <div class="header-actions">
            <el-tag v-if="dirty" type="warning" size="small">有未保存的修改</el-tag>
            <el-button :disabled="!workstations.length" @click="openCopyDialog">从其他日期类型复制</el-button>
            <el-button :disabled="!workstations.length" @click="downloadTemplate">下载模板</el-button>
            <el-upload
              :auto-upload="false"
              :show-file-list="false"
              accept=".xlsx,.xls,.csv"
              :on-change="handleFileChange"
              style="display: inline-flex"
            >
              <el-button type="primary" plain>上传 Excel 导入</el-button>
            </el-upload>
            <el-upload
              :auto-upload="false"
              :show-file-list="false"
              accept=".xlsx,.xls,.csv,.png,.jpg,.jpeg,.webp"
              :on-change="handleAiFileChange"
              style="display: inline-flex"
            >
              <el-button type="primary" plain :loading="aiParsing">AI 识别导入</el-button>
            </el-upload>
            <el-button @click="openAiSettings">AI 设置</el-button>
            <el-button type="primary" :disabled="!dirty || saving" :loading="saving" @click="handleSave">保存</el-button>
          </div>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="时间轴为营业时段 12:00 至次日 06:00（00:00 起为次日；06:00-11:30 闭店时段不在图中，其数据保留）。单元格含义：单数字 N = 该时段该岗位最少 N 人；写成 M,N（如 2,3）= 最少 M 人、最好 N 人。一键排班优先保证最少人数，人手有余时尽量补到最好人数。点击单元格编辑；按住拖动框选区域后可批量修改人数并附备注（Esc 或点空白处取消选区）；带备注的格子右上角有橙点，悬停可查看。"
        style="margin-bottom: 12px"
      />

      <div class="stats-bar">
        <div class="stat-box">
          <div class="stat-label">最少总需求（{{ labelOf(activeDayType) }}）</div>
          <div class="stat-value">{{ stats.minHours }} <span class="stat-unit">人·时</span></div>
          <div class="stat-sub">= {{ stats.minSlots }} 人·半小时</div>
        </div>
        <div class="stat-box">
          <div class="stat-label">最好总需求（{{ labelOf(activeDayType) }}）</div>
          <div class="stat-value">{{ stats.idealHours }} <span class="stat-unit">人·时</span></div>
          <div class="stat-sub">= {{ stats.idealSlots }} 人·半小时</div>
        </div>
        <div class="stat-box">
          <div class="stat-label">峰值并发 · 最少</div>
          <div class="stat-value">{{ stats.peakMin }} <span class="stat-unit">人</span></div>
          <div class="stat-sub">出现在 {{ stats.peakMinSlot }} 时段</div>
        </div>
        <div class="stat-box">
          <div class="stat-label">峰值并发 · 最好</div>
          <div class="stat-value">{{ stats.peakIdeal }} <span class="stat-unit">人</span></div>
          <div class="stat-sub">出现在 {{ stats.peakIdealSlot }} 时段</div>
        </div>
        <div class="stat-box total">
          <div class="stat-label">三档合计 · 最少</div>
          <div class="stat-value">{{ totalStats.minHours }} <span class="stat-unit">人·时/天</span></div>
          <div class="stat-sub">平日 {{ totalStats.byType.WORKDAY }} · 周末 {{ totalStats.byType.WEEKEND }} · 节假日 {{ totalStats.byType.HOLIDAY }}</div>
        </div>
      </div>

      <el-tabs v-model="activeDayType">
        <el-tab-pane v-for="dt in DAY_TYPES" :key="dt.value" :label="dt.label" :name="dt.value" />
      </el-tabs>

      <div v-loading="loading" class="gantt-wrap" @scroll="closeEditor">
        <div class="gantt-inner">
          <div class="gantt-header">
            <div class="gantt-corner">工作站 \ 时段</div>
            <div class="gantt-axis">
              <div
                v-for="(row, si) in displayRows"
                :key="row.slot"
                class="axis-cell"
                :class="{ hour: row.slot.endsWith(':00'), midnight: si === NIGHT_START, 'open-time': si === OPEN_INDEX }"
              >{{ si === NIGHT_START ? '次日' : (si === OPEN_INDEX ? '开门' : (row.slot.endsWith(':00') ? row.slot.slice(0, 2) : '')) }}</div>
            </div>
          </div>
          <div v-for="(ws, wi) in workstations" :key="ws.id" class="gantt-row">
            <div class="gantt-label" :title="ws.name">{{ ws.name }}</div>
            <div class="gantt-cells">
              <div
                v-for="(row, si) in displayRows"
                :key="row.slot"
                class="gantt-cell"
                :class="[cellClass(row, ws.id), isSelected(si, wi) ? 'selected' : '', si === NIGHT_START ? 'midnight' : '', si === OPEN_INDEX ? 'open-time' : '', hasRemark(row, ws.id) ? 'has-remark' : '']"
                :data-si="si"
                :data-wi="wi"
                @mousedown.prevent="onCellDown($event, si, wi)"
                @mouseenter="onCellEnter(si, wi); onCellHoverEnter(si, wi)"
                @mousemove="onCellHoverMove"
                @mouseleave="onCellHoverLeave"
              >{{ cellText(row, ws.id) }}</div>
            </div>
          </div>
        </div>

      </div>

      <div class="legend">
        <span class="legend-title" style="color: #d97706">┃ 开门 17:00</span>
        <span class="legend-title" style="color: #4c6fff">┃ 次日 00:00</span>
        <span class="legend-title">颜色深浅 = 最少人数：</span>
        <span class="sw lv0">0</span>
        <span class="sw lv1">1</span>
        <span class="sw lv2">2</span>
        <span class="sw lv3">3</span>
        <span class="sw lv4">4+</span>
        <span class="legend-title" style="margin-left: 18px">斜纹 = 有「最好」目标：</span>
        <span class="sw lv2 soft">2,3</span>
        <span class="legend-title" style="margin-left: 18px">● 角标 = 有备注（悬停查看）：</span>
        <span class="sw lv1" style="position: relative">1<i style="position: absolute; top: 2px; right: 2px; width: 5px; height: 5px; border-radius: 50%; background: #e6a23c"></i></span>
      </div>

      <div
        v-if="editor.visible"
        class="cell-editor"
        :style="{ left: editor.x + 'px', top: editor.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <div class="editor-title">{{ editor.wsName }} · {{ editor.slot }}</div>
        <div class="editor-row">
          <span class="editor-label">最少</span>
          <el-input-number v-model="editor.min" :min="0" :max="99" size="small" :controls="false" style="width: 96px" />
        </div>
        <div class="editor-row">
          <span class="editor-label">最好</span>
          <el-input-number v-model="editor.ideal" :min="0" :max="99" size="small" :controls="false" style="width: 96px" />
        </div>
        <div class="editor-row">
          <span class="editor-label">备注</span>
          <el-input v-model="editor.remark" size="small" maxlength="200" placeholder="可选" style="width: 156px" />
        </div>
        <div class="editor-quick">
          <el-button v-for="n in [0, 1, 2, 3]" :key="n" size="small" @click="quickSet(n)">{{ n }}</el-button>
        </div>
        <div class="editor-actions">
          <el-button size="small" @click="closeEditor">取消</el-button>
          <el-button size="small" type="primary" @click="applyEditor">确定</el-button>
        </div>
      </div>

      <div
        v-if="batch.visible"
        class="cell-editor batch-toolbar"
        :style="{ left: batch.x + 'px', top: batch.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <div class="editor-title">批量修改 · 已选 {{ batch.count }} 格</div>
        <div class="editor-row">
          <span class="editor-label">最少</span>
          <el-input-number v-model="batch.min" :min="0" :max="99" size="small" :controls="false" style="width: 96px" />
        </div>
        <div class="editor-row">
          <span class="editor-label">最好</span>
          <el-input-number v-model="batch.ideal" :min="0" :max="99" size="small" :controls="false" style="width: 96px" />
        </div>
        <div class="editor-row">
          <span class="editor-label">备注</span>
          <el-input v-model="batch.remark" size="small" maxlength="200" placeholder="留空=清除选区备注" style="width: 156px" />
        </div>
        <div class="editor-quick">
          <el-button v-for="n in [0, 1, 2, 3]" :key="n" size="small" @click="quickBatchSet(n)">{{ n }}</el-button>
        </div>
        <div class="editor-actions">
          <el-button size="small" @click="clearSelection">取消</el-button>
          <el-button size="small" type="primary" @click="applyBatch">应用到选区</el-button>
        </div>
      </div>

      <div
        v-if="hoverTip.visible"
        class="cell-tip"
        :style="{ left: hoverTip.x + 'px', top: hoverTip.y + 'px' }"
      >
        <div class="tip-title">{{ hoverTip.title }}</div>
        <div v-if="hoverTip.demand" class="tip-demand">{{ hoverTip.demand }}</div>
        <div v-if="hoverTip.remark" class="tip-remark">备注：{{ hoverTip.remark }}</div>
      </div>
    </el-card>

    <el-dialog v-model="aiSettingsVisible" title="AI 识别设置（DeepSeek）" width="520px">
      <el-form label-width="100px">
        <el-form-item label="API Key">
          <el-input v-model="aiForm.apiKey" type="password" show-password placeholder="sk-...（留空表示不修改）" />
        </el-form-item>
        <el-form-item label="接口地址">
          <el-input v-model="aiForm.baseUrl" placeholder="https://api.deepseek.com" />
        </el-form-item>
        <el-form-item label="模型">
          <el-input v-model="aiForm.model" placeholder="deepseek-chat" />
        </el-form-item>
        <el-alert
          type="info"
          :closable="false"
          show-icon
          :title="aiConfigHint"
          style="margin-bottom: 8px"
        />
        <el-alert
          type="warning"
          :closable="false"
          show-icon
          title="表格（Excel/CSV）识别所有文本模型均支持；图片识别需要模型支持视觉输入（如 deepseek-vl 系列），当前默认模型仅支持表格。"
          style="margin-bottom: 8px"
        />
        <el-form-item label=" ">
          <el-button size="small" :loading="aiTesting" @click="handleAiTest">测试连接</el-button>
          <span v-if="aiTestResult" style="margin-left: 8px; font-size: 13px; color: #606266">{{ aiTestResult }}</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="aiSettingsVisible = false">取消</el-button>
        <el-button type="primary" :loading="aiSaving" @click="handleAiSave">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="copyDialogVisible" title="从其他日期类型复制" width="420px">
      <el-form label-width="100px">
        <el-form-item label="源类型">
          <el-select v-model="copySource" style="width: 100%">
            <el-option v-for="dt in otherDayTypes" :key="dt.value" :label="dt.label" :value="dt.value" />
          </el-select>
        </el-form-item>
        <el-alert
          type="warning"
          :closable="false"
          show-icon
          title="复制会覆盖当前标签页的全部数据，仍需点击「保存」才会写入数据库。"
        />
      </el-form>
      <template #footer>
        <el-button @click="copyDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="applyCopy">复制</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getWorkstations } from '../api/workstations'
import { getStaffingRequirements, saveStaffingRequirements } from '../api/staffingRequirements'
import { getAiConfig, saveAiConfig, testAi, aiParseRequirementDoc } from '../api/ai'

const DAY_TYPES = [
  { value: 'WORKDAY', label: '平日' },
  { value: 'WEEKEND', label: '周末' },
  { value: 'HOLIDAY', label: '节假日' }
]

const DAY_TYPE_KEYWORDS = [
  { value: 'WORKDAY', re: /平日|平常|工作日|WORKDAY|WD/i },
  { value: 'WEEKEND', re: /周末|周六|周日|星期六|星期天|WEEKEND|WE/i },
  { value: 'HOLIDAY', re: /节假日|假期|节日|法定|HOLIDAY|HD/i }
]

const SLOTS = Array.from({ length: 48 }, (_, i) => {
  const h = String(Math.floor(i / 2)).padStart(2, '0')
  const m = i % 2 === 0 ? '00' : '30'
  return [h, m].join(':')
})

// 显示窗口：营业时段 12:00 起 → 次日 06:00 止（00:00-05:30 为次日；06:00-11:30 闭店时段隐藏，数据保留）
// SLOTS 索引 24-47 = 12:00-23:30，索引 0-11 = 次日 00:00-05:30
const SLOT_ORDER = (() => {
  const order = []
  for (let i = 24; i < 48; i++) order.push(i)
  for (let i = 0; i < 12; i++) order.push(i)
  return order
})()
const NIGHT_START = 24 // displayRows 中「次日 00:00」的位置
const OPEN_INDEX = SLOT_ORDER.findIndex(i => SLOTS[i] === '17:00') // 开门营业时间 17:00 在显示序中的位置

function emptyMatrix() {
  return SLOTS.map(slot => ({ slot, cells: {} }))
}

const loading = ref(false)
const saving = ref(false)
const workstations = ref([])
const activeDayType = ref('WORKDAY')
const matrices = ref({ WORKDAY: emptyMatrix(), WEEKEND: emptyMatrix(), HOLIDAY: emptyMatrix() })
const lastSaved = ref({ WORKDAY: '', WEEKEND: '', HOLIDAY: '' })
const dirty = ref(false)
const copyDialogVisible = ref(false)
const copySource = ref('')

// ============ AI 识别 ============
const aiSettingsVisible = ref(false)
const aiForm = reactive({ apiKey: '', baseUrl: 'https://api.deepseek.com', model: 'deepseek-chat' })
const aiConfigHint = ref('')
const aiSaving = ref(false)
const aiTesting = ref(false)
const aiTestResult = ref('')
const aiParsing = ref(false)

// 单元格编辑器
const editor = reactive({ visible: false, si: -1, wi: -1, wsId: 0, wsName: '', slot: '', min: 0, ideal: 0, remark: '', x: 0, y: 0 })

// 拖动框选（非响应式拖动状态 + 响应式选区）
const dragState = { active: false, moved: false, startSi: -1, startWi: -1, curSi: -1, curWi: -1 }
const dragRange = ref(null) // 拖动中的实时选区
const selection = ref(null) // 已确定的选区 {s1, s2, w1, w2}
const batch = reactive({ visible: false, min: null, ideal: null, remark: '', count: 0, x: 0, y: 0 })
// 悬浮提示
const hoverTip = reactive({ visible: false, x: 0, y: 0, title: '', demand: '', remark: '' })
let dragAnchorEl = null

// 按营业时段顺序展示的行（对象与矩阵共享引用，编辑直接生效）
const displayRows = computed(() => {
  const all = matrices.value[activeDayType.value]
  return SLOT_ORDER.map(i => all[i])
})
const otherDayTypes = computed(() => DAY_TYPES.filter(d => d.value !== activeDayType.value))

// ============ 需求统计 ============
function tabStats(dayType) {
  const all = matrices.value[dayType]
  let minSlots = 0
  let idealSlots = 0
  let peakMin = 0
  let peakIdeal = 0
  let peakMinSlot = '--'
  let peakIdealSlot = '--'
  // 只统计展示窗口（营业时段 12:00~次日 05:30），隐藏的闭店时段不计入
  for (const idx of SLOT_ORDER) {
    const row = all[idx]
    if (!row) continue
    let sumMin = 0
    let sumIdeal = 0
    for (const ws of workstations.value) {
      const c = row.cells[ws.id] || {}
      sumMin += c.min || 0
      sumIdeal += Math.max(c.ideal || 0, c.min || 0)
    }
    minSlots += sumMin
    idealSlots += sumIdeal
    if (sumMin > peakMin) {
      peakMin = sumMin
      peakMinSlot = row.slot
    }
    if (sumIdeal > peakIdeal) {
      peakIdeal = sumIdeal
      peakIdealSlot = row.slot
    }
  }
  return { minSlots, idealSlots, peakMin, peakIdeal, peakMinSlot, peakIdealSlot }
}

const stats = computed(() => {
  const s = tabStats(activeDayType.value)
  return {
    ...s,
    minHours: (s.minSlots * 0.5).toFixed(1),
    idealHours: (s.idealSlots * 0.5).toFixed(1)
  }
})

const totalStats = computed(() => {
  const byType = {}
  let minHours = 0
  let idealHours = 0
  for (const dt of DAY_TYPES) {
    const s = tabStats(dt.value)
    byType[dt.value] = (s.minSlots * 0.5).toFixed(1)
    minHours += s.minSlots * 0.5
    idealHours += s.idealSlots * 0.5
  }
  return { byType, minHours: minHours.toFixed(1), idealHours: idealHours.toFixed(1) }
})

watch(
  () => matrices.value,
  () => {
    dirty.value = DAY_TYPES.some(d => lastSaved.value[d.value] !== JSON.stringify(matrices.value[d.value]))
  },
  { deep: true }
)

watch(activeDayType, () => closeEditor())

function labelOf(dayType) {
  return DAY_TYPES.find(d => d.value === dayType)?.label ?? dayType
}

function fmtTime(t) {
  if (!t) return '--'
  const m = String(t).match(/[0-9]{2}:[0-9]{2}/)
  return m ? m[0] : '--'
}

async function loadData() {
  loading.value = true
  try {
    const [wsList, reqList] = await Promise.all([getWorkstations(), getStaffingRequirements()])
    workstations.value = (wsList || [])
      .filter(w => w.status === 1)
      .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.id - b.id)

    const m = { WORKDAY: emptyMatrix(), WEEKEND: emptyMatrix(), HOLIDAY: emptyMatrix() }
    for (const item of reqList || []) {
      const matrix = m[item.dayType]
      if (!matrix) continue
      const slot = fmtTime(item.timeSlot)
      const row = matrix.find(r => r.slot === slot)
      if (row) {
        row.cells[item.workstationId] = {
          min: item.requiredCount || 0,
          ideal: Math.max(item.idealCount || 0, item.requiredCount || 0),
          remark: item.remark || ''
        }
      }
    }
    matrices.value = m
    for (const d of DAY_TYPES) lastSaved.value[d.value] = JSON.stringify(m[d.value])
    dirty.value = false
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    loading.value = false
  }
}

function cellInfo(row, wsId) {
  const c = row.cells[wsId]
  return { min: c ? c.min || 0 : 0, ideal: c ? c.ideal || 0 : 0, remark: c ? c.remark || '' : '' }
}

function hasRemark(row, wsId) {
  return !!cellInfo(row, wsId).remark
}

function cellTitle(row, wsId) {
  const { min, ideal, remark } = cellInfo(row, wsId)
  if (min === 0 && ideal === 0 && !remark) return ''
  const demand = min === 0 && ideal === 0 ? '未配置' : '最少 ' + min + ' 人' + (ideal > min ? '，最好 ' + ideal + ' 人' : '')
  return remark ? demand + '｜备注：' + remark : demand
}

function cellClass(row, wsId) {
  const { min, ideal } = cellInfo(row, wsId)
  let cls = 'lv' + Math.min(min, 4)
  if (min < ideal) cls += ' soft'
  return cls
}

function cellText(row, wsId) {
  const { min, ideal } = cellInfo(row, wsId)
  if (min === 0 && ideal === 0) return ''
  return min === ideal ? String(min) : min + ',' + ideal
}

// ============ 点选编辑 ============
function openEditor(si, wi, anchorEl) {
  const ws = workstations.value[wi]
  const displayRow = displayRows.value[si]
  const cell = displayRow.cells[ws.id] || { min: 0, ideal: 0 }
  const rect = anchorEl.getBoundingClientRect()
  let x = rect.left + 6
  let y = rect.top + rect.height + 6
  if (x > window.innerWidth - 240) x = window.innerWidth - 240
  if (y > window.innerHeight - 250) y = Math.max(8, rect.top - 250)
  editor.visible = true
  editor.si = si
  editor.wi = wi
  editor.wsId = ws.id
  editor.wsName = ws.name
  editor.slot = (si >= NIGHT_START ? '次日 ' : '') + displayRow.slot
  editor.min = cell.min || 0
  editor.ideal = Math.max(cell.ideal || 0, cell.min || 0)
  editor.remark = cell.remark || ''
  editor.x = x
  editor.y = y
}

function closeEditor() {
  editor.visible = false
  dragRange.value = null
  dragState.active = false
  dragState.moved = false
  clearSelection()
  hideTip()
}

function clearSelection() {
  selection.value = null
  batch.visible = false
}

function quickSet(n) {
  editor.min = n
  editor.ideal = n
}

function applyEditor() {
  const min = Math.max(0, Math.min(99, Math.round(editor.min || 0)))
  const ideal = Math.max(min, Math.min(99, Math.round(editor.ideal || 0)))
  const remark = (editor.remark || '').trim()
  const row = displayRows.value[editor.si]
  row.cells[editor.wsId] = { min, ideal, remark }
  closeEditor()
}

// ============ 拖动框选 ============
function onCellDown(e, si, wi) {
  closeEditor()
  dragAnchorEl = e.currentTarget
  dragState.active = true
  dragState.moved = false
  dragState.startSi = si
  dragState.startWi = wi
  dragState.curSi = si
  dragState.curWi = wi
  updateDragRange()
}

function onCellEnter(si, wi) {
  if (!dragState.active) return
  if (si !== dragState.curSi || wi !== dragState.curWi) dragState.moved = true
  dragState.curSi = si
  dragState.curWi = wi
  updateDragRange()
}

// ============ 悬浮提示（悬停显示需求与备注） ============
function onCellHoverEnter(si, wi) {
  if (dragState.active) {
    hideTip()
    return
  }
  const ws = workstations.value[wi]
  const row = displayRows.value[si]
  const { min, ideal, remark } = cellInfo(row, ws.id)
  if (min === 0 && ideal === 0 && !remark) {
    hideTip()
    return
  }
  hoverTip.title = ws.name + ' · ' + (si >= NIGHT_START ? '次日 ' : '') + row.slot
  hoverTip.demand = min === 0 && ideal === 0 ? '' : '最少 ' + min + ' 人' + (ideal > min ? '，最好 ' + ideal + ' 人' : '')
  hoverTip.remark = remark
  hoverTip.visible = true
}

function onCellHoverMove(e) {
  if (!hoverTip.visible) return
  let x = e.clientX + 14
  let y = e.clientY + 18
  if (x > window.innerWidth - 230) x = e.clientX - 218
  if (y > window.innerHeight - 96) y = e.clientY - 88
  hoverTip.x = x
  hoverTip.y = y
}

function onCellHoverLeave() {
  hideTip()
}

function hideTip() {
  hoverTip.visible = false
}

function updateDragRange() {
  dragRange.value = {
    s1: Math.min(dragState.startSi, dragState.curSi),
    s2: Math.max(dragState.startSi, dragState.curSi),
    w1: Math.min(dragState.startWi, dragState.curWi),
    w2: Math.max(dragState.startWi, dragState.curWi)
  }
}

function isSelected(si, wi) {
  const r = dragState.active ? dragRange.value : selection.value
  return !!(r && si >= r.s1 && si <= r.s2 && wi >= r.w1 && wi <= r.w2)
}

function finishDrag() {
  if (!dragState.active) return
  if (dragState.moved && dragRange.value) {
    openBatchToolbar(dragRange.value)
  } else {
    openEditor(dragState.startSi, dragState.startWi, dragAnchorEl)
  }
  dragState.active = false
  dragState.moved = false
  dragRange.value = null
  dragAnchorEl = null
}

// ============ 批量修改 ============
function openBatchToolbar(r) {
  const rows = displayRows.value
  const wsList = workstations.value
  const cells = []
  for (let wi = r.w1; wi <= r.w2; wi++) {
    for (let si = r.s1; si <= r.s2; si++) {
      const c = rows[si].cells[wsList[wi].id] || {}
      cells.push({ min: c.min || 0, ideal: Math.max(c.ideal || 0, c.min || 0), remark: c.remark || '' })
    }
  }
  // 所有格同值时预填，否则留空提示用户输入
  const same = cells.every(c => c.min === cells[0].min && c.ideal === cells[0].ideal)
  const sameRemark = cells.every(c => c.remark === cells[0].remark)
  batch.min = same ? cells[0].min : null
  batch.ideal = same ? cells[0].ideal : null
  batch.remark = sameRemark ? cells[0].remark : ''
  batch.count = cells.length
  selection.value = { ...r }

  // 定位到选区右下角格子下方
  const wrap = document.querySelector('.gantt-wrap')
  const cellEl = wrap?.querySelector('[data-si="' + r.s2 + '"][data-wi="' + r.w2 + '"]')
  const rect = cellEl?.getBoundingClientRect()
  let x = rect ? rect.right - 250 : 12
  let y = rect ? rect.bottom + 6 : 12
  if (x < 8) x = 8
  if (x > window.innerWidth - 260) x = window.innerWidth - 260
  if (y > window.innerHeight - 250) y = Math.max(8, (rect?.top ?? 0) - 250)
  batch.x = x
  batch.y = y
  batch.visible = true
}

function quickBatchSet(n) {
  batch.min = n
  batch.ideal = n
}

function applyBatch() {
  const r = selection.value
  if (!r) return
  if (batch.min == null || batch.ideal == null) {
    ElMessage.warning('请填写最少和最好人数')
    return
  }
  const min = Math.max(0, Math.min(99, Math.round(batch.min)))
  const ideal = Math.max(min, Math.min(99, Math.round(batch.ideal)))
  const remark = (batch.remark || '').trim()
  const rows = displayRows.value
  const wsList = workstations.value
  for (let wi = r.w1; wi <= r.w2; wi++) {
    for (let si = r.s1; si <= r.s2; si++) {
      rows[si].cells[wsList[wi].id] = { min, ideal, remark }
    }
  }
  ElMessage.success('已批量修改 ' + batch.count + ' 格' + (remark ? '（含备注）' : '') + '，点击「保存」后生效')
  clearSelection()
}

function onGlobalMouseDown(e) {
  if (!batch.visible) return
  const t = e.target
  if (t && typeof t.closest === 'function' && (t.closest('.batch-toolbar') || t.closest('.gantt-cell'))) return
  clearSelection()
}

function onKeydown(e) {
  if (e.key === 'Escape') closeEditor()
}

async function handleSave() {
  if (saving.value) return
  const dayType = activeDayType.value
  const entries = []
  for (const row of matrices.value[dayType]) {
    for (const ws of workstations.value) {
      const c = row.cells[ws.id] || { min: 0, ideal: 0, remark: '' }
      entries.push({
        workstationId: ws.id,
        timeSlot: row.slot,
        requiredCount: c.min || 0,
        idealCount: Math.max(c.ideal || 0, c.min || 0),
        remark: (c.remark || '').trim() || null
      })
    }
  }
  saving.value = true
  try {
    await saveStaffingRequirements({ dayType, entries })
    lastSaved.value[dayType] = JSON.stringify(matrices.value[dayType])
    dirty.value = DAY_TYPES.some(d => lastSaved.value[d.value] !== JSON.stringify(matrices.value[d.value]))
    ElMessage.success('「' + labelOf(dayType) + '」人数需求已保存')
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    saving.value = false
  }
}

// ============ 复制配置 ============
function openCopyDialog() {
  copySource.value = otherDayTypes.value[0]?.value ?? ''
  copyDialogVisible.value = true
}

async function applyCopy() {
  if (!copySource.value) return
  try {
    await ElMessageBox.confirm(
      '确定将「' + labelOf(copySource.value) + '」的配置复制到「' + labelOf(activeDayType.value) + '」并覆盖当前数据吗？',
      '提示',
      { confirmButtonText: '复制', cancelButtonText: '取消', type: 'warning' }
    )
  } catch {
    return
  }
  const src = matrices.value[copySource.value]
  const dst = matrices.value[activeDayType.value]
  src.forEach((row, i) => {
    dst[i].cells = { ...row.cells }
  })
  copyDialogVisible.value = false
  ElMessage.success('已复制，点击「保存」后生效')
}

// ============ Excel 模板下载 ============
async function downloadTemplate() {
  const XLSX = await import('xlsx')
  const wb = XLSX.utils.book_new()

  const readme = [
    ['人数需求导入模板说明'],
    ['1. 本模板含「平日」「周末」「节假日」三个工作表，均为 时段 × 工作站 矩阵（营业时段 12:00 至次日 06:00，30 分钟对齐），直接填人数后上传即可。'],
    ['2. 单元格两种写法：单数字 N = 最少且最好 N 人；M,N（如 2,3）= 最少 M 人、最好 N 人。'],
    ['3. 也可使用长表格式（任意表名）：四列——日期类型 | 工作站 | 时段 | 需求人数（需求人数列同样支持 2,3 写法）。'],
    ['4. 日期类型可写：平日/工作日、周末/周六日、节假日（或 WORKDAY/WEEKEND/HOLIDAY）。'],
    ['5. 工作站列可填工作站名称或编号；时段需 30 分钟对齐，如 08:00、18:30（也支持 08:00-08:30 取开始时间）。'],
    ['6. 人数为 0-99 的整数，留空视为 0。'],
    ['7. 上传后仅并入页面编辑器，需点击「保存」才会写入数据库并影响一键排班。']
  ]
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(readme), '使用说明')

  for (const dt of DAY_TYPES) {
    const matrix = [['时段', ...workstations.value.map(w => w.name)]]
    for (const row of SLOT_ORDER.map(i => matrices.value[dt.value][i])) {
      matrix.push([
        row.slot,
        ...workstations.value.map(w => {
          const c = row.cells[w.id] || { min: 0, ideal: 0 }
          const min = c.min || 0
          const ideal = Math.max(c.ideal || 0, min)
          return min === ideal ? min : min + ',' + ideal
        })
      ])
    }
    XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(matrix), dt.label)
  }

  XLSX.writeFile(wb, '人数需求导入模板.xlsx')
}

// ============ Excel 上传解析 ============
function handleFileChange(uploadFile) {
  const file = uploadFile?.raw
  if (!file) return
  parseExcel(file)
}

async function parseExcel(file) {
  try {
    const XLSX = await import('xlsx')
    const data = await file.arrayBuffer()
    const wb = XLSX.read(data, { type: 'array' })
    const out = { rows: [], errors: [] }

    for (const sheetName of wb.SheetNames) {
      if (/说明|readme/i.test(sheetName)) continue
      const sheet = wb.Sheets[sheetName]
      const rows = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: '' })
      parseSheet(rows, sheetName, out)
    }

    applyParsed(out)
  } catch (e) {
    ElMessage.error('文件解析失败：' + (e?.message || e))
  }
}

function parseSheet(rows, sheetName, out) {
  let headerIdx = -1
  for (let i = 0; i < rows.length; i++) {
    const r = rows[i]
    if (r && r.some(c => c !== '' && c != null)) {
      headerIdx = i
      break
    }
  }
  if (headerIdx < 0) return

  const header = rows[headerIdx].map(c => String(c ?? '').trim())
  const hasType = header.some(h => /类型|day/i.test(h))
  const hasWs = header.some(h => /工作站|岗位|工位|station|workstation/i.test(h))
  const hasSlot = header.some(h => /时段|时间|slot/i.test(h))
  const hasCount = header.some(h => /需求|人数|count|required/i.test(h))

  if (hasType && hasWs && hasSlot && hasCount) {
    parseLongFormat(rows, header, headerIdx, sheetName, out)
  } else {
    // 正向判断矩阵：首列为时间/时段，且其余列至少一列能解析为工作站
    const firstIsSlot = header.length > 0 && /时段|时间|slot/i.test(header[0])
    const restResolvable = header.slice(1).filter(Boolean).some(h => resolveColumnHeader(h).ws)
    if (firstIsSlot && restResolvable) {
      parseMatrixFormat(rows, header, headerIdx, sheetName, out)
    } else {
      out.errors.push('工作表「' + sheetName + '」格式无法识别（支持：模板矩阵或 日期类型/工作站/时段/需求人数 长表）')
    }
  }
}

function parseLongFormat(rows, header, headerIdx, sheetName, out) {
  const idx = {
    dayType: header.findIndex(h => /类型|day/i.test(h)),
    ws: header.findIndex(h => /工作站|岗位|工位|station|workstation/i.test(h)),
    slot: header.findIndex(h => /时段|时间|slot/i.test(h)),
    count: header.findIndex(h => /需求|人数|count|required/i.test(h))
  }

  for (let i = headerIdx + 1; i < rows.length; i++) {
    const r = rows[i]
    if (!r || r.every(c => c === '' || c == null)) continue
    const dayType = normalizeDayType(String(r[idx.dayType] ?? '').trim())
    if (!dayType) {
      out.errors.push('第 ' + (i + 1) + ' 行：日期类型「' + r[idx.dayType] + '」无法识别')
      continue
    }
    const slot = normalizeSlot(r[idx.slot])
    if (!slot) {
      out.errors.push('第 ' + (i + 1) + ' 行：时段「' + r[idx.slot] + '」无效（需 30 分钟对齐）')
      continue
    }
    const ws = resolveWorkstation(r[idx.ws])
    if (!ws) {
      out.errors.push('第 ' + (i + 1) + ' 行：工作站「' + r[idx.ws] + '」不存在')
      continue
    }
    const demand = toDemand(r[idx.count])
    if (!demand) {
      out.errors.push('第 ' + (i + 1) + ' 行：人数「' + r[idx.count] + '」无效（需 0-99 整数或 M,N 两档）')
      continue
    }
    out.rows.push({ dayType, workstationId: ws.id, slot, min: demand.min, ideal: demand.ideal })
  }
}

function parseMatrixFormat(rows, header, headerIdx, sheetName, out) {
  const sheetType = normalizeDayType(sheetName)
  const columns = []
  for (let c = 1; c < header.length; c++) {
    if (!header[c]) continue
    const resolved = resolveColumnHeader(header[c])
    if (!resolved.ws) {
      out.errors.push('工作表「' + sheetName + '」列「' + header[c] + '」的工作站无法识别，已跳过')
      continue
    }
    columns.push({ ws: resolved.ws, dayType: resolved.colType || sheetType, col: c })
  }
  if (columns.length === 0) {
    out.errors.push('工作表「' + sheetName + '」没有可识别的工作站列')
    return
  }

  for (let i = headerIdx + 1; i < rows.length; i++) {
    const r = rows[i]
    if (!r) continue
    const first = String(r[0] ?? '').trim()
    if (first === '') continue
    const slot = normalizeSlot(r[0])
    if (!slot) {
      out.errors.push('第 ' + (i + 1) + ' 行：时段「' + r[0] + '」无效（需 30 分钟对齐）')
      continue
    }
    for (const col of columns) {
      const demand = toDemand(r[col.col])
      if (!demand) {
        out.errors.push('第 ' + (i + 1) + ' 行「' + col.ws.name + '」列：人数「' + r[col.col] + '」无效（需 0-99 整数或 M,N 两档）')
        continue
      }
      out.rows.push({ dayType: col.dayType, workstationId: col.ws.id, slot, min: demand.min, ideal: demand.ideal })
    }
  }
}

function resolveColumnHeader(raw) {
  const direct = resolveWorkstation(raw)
  if (direct) return { ws: direct, colType: null }

  const parts = String(raw).split(/[-—–_ ]+/).filter(Boolean)
  for (let i = 1; i <= 2 && i < parts.length; i++) {
    const head = parts.slice(0, parts.length - i).join('')
    const tail = parts.slice(parts.length - i).join('')
    const t = normalizeDayType(tail)
    if (t) {
      const w = resolveWorkstation(head)
      if (w) return { ws: w, colType: t }
    }
  }
  return { ws: null, colType: null }
}

function resolveWorkstation(name) {
  const s = String(name ?? '').trim()
  if (!s) return null
  const norm = s.toLowerCase()
  let w = workstations.value.find(x => x.name?.trim() === s || x.name?.trim().toLowerCase() === norm)
  if (w) return w
  w = workstations.value.find(x => x.code?.toLowerCase() === norm)
  if (w) return w
  const stripped = s.replace(/工作站|工位|岗位/g, '').trim()
  if (stripped && stripped !== s) {
    const n2 = stripped.toLowerCase()
    w = workstations.value.find(x => x.name?.toLowerCase() === n2)
    if (w) return w
  }
  return null
}

function normalizeDayType(s) {
  if (!s) return null
  for (const k of DAY_TYPE_KEYWORDS) {
    if (k.re.test(s)) return k.value
  }
  return null
}

function normalizeSlot(v) {
  if (v == null || v === '') return null
  if (typeof v === 'number') {
    // Excel 时间序列值（0-1 之间的小数）
    const mins = Math.round(v * 24 * 60)
    if (mins < 0 || mins >= 1440 || mins % 30 !== 0) return null
    const h = String(Math.floor(mins / 60)).padStart(2, '0')
    const m = String(mins % 60).padStart(2, '0')
    return h + ':' + m
  }
  const s = String(v).trim()
  const m = s.match(/([0-9]{1,2}):([0-9]{2})(?::[0-9]{2})?/)
  if (!m) return null
  const h = Number(m[1])
  const min = Number(m[2])
  if (h > 23 || min > 59 || min % 30 !== 0) return null
  return String(h).padStart(2, '0') + ':' + String(min).padStart(2, '0')
}

// 需求单元格解析：'3' -> {3,3}；'2,3' / '(2,3)' / '2-3' -> {2,3}；空 -> {0,0}
function toDemand(v) {
  if (v === '' || v == null) return { min: 0, ideal: 0 }
  if (typeof v === 'number') {
    const n = Math.round(v)
    if (n < 0 || n > 99) return null
    return { min: n, ideal: n }
  }
  let s = String(v).trim()
  if (/^[0-9]+$/.test(s)) {
    const n = Number(s)
    if (n > 99) return null
    return { min: n, ideal: n }
  }
  // 去掉可能的括号：'(2,3)' -> '2,3'
  if (s.startsWith('(')) s = s.slice(1)
  if (s.endsWith(')')) s = s.slice(0, -1)
  const m = s.match(/^([0-9]+)[ ]*[,，、~-][ ]*([0-9]+)$/)
  if (m) {
    const a = Number(m[1])
    const b = Number(m[2])
    const min = Math.min(a, b)
    const ideal = Math.max(a, b)
    if (ideal > 99) return null
    return { min, ideal }
  }
  return null
}

// ============ AI 设置 ============
async function openAiSettings() {
  aiTestResult.value = ''
  aiForm.apiKey = ''
  aiConfigHint.value = '读取配置中…'
  try {
    const cfg = await getAiConfig()
    aiForm.baseUrl = cfg.baseUrl || 'https://api.deepseek.com'
    aiForm.model = cfg.model || 'deepseek-chat'
    aiConfigHint.value = cfg.configured ? '已配置 Key：' + cfg.maskedKey : '尚未配置 API Key，请填入 DeepSeek Key 后保存'
  } catch {
    aiConfigHint.value = '获取配置失败'
  }
  aiSettingsVisible.value = true
}

// 仅允许官方 DeepSeek 域名，避免任意地址劫持 API Key（后端校验需后端配合）
function isOfficialDeepSeekUrl(url) {
  try {
    const host = new URL(url).hostname.toLowerCase()
    return host === 'api.deepseek.com' || host === 'api.deepseek.com.cn'
  } catch {
    return false
  }
}

async function handleAiSave() {
  if (!aiForm.baseUrl || !aiForm.model) {
    ElMessage.warning('接口地址和模型不能为空')
    return
  }
  if (!isOfficialDeepSeekUrl(aiForm.baseUrl)) {
    try {
      await ElMessageBox.confirm(
        '接口地址不是官方 DeepSeek 域名（api.deepseek.com），使用非官方地址存在 API Key 泄露风险。确认仍要保存吗？',
        '安全提示',
        { confirmButtonText: '仍要保存', cancelButtonText: '取消', type: 'warning' }
      )
    } catch {
      return
    }
  }
  aiSaving.value = true
  try {
    const cfg = await saveAiConfig({
      apiKey: aiForm.apiKey || undefined,
      baseUrl: aiForm.baseUrl,
      model: aiForm.model
    })
    aiConfigHint.value = '已配置 Key：' + (cfg.maskedKey || '未设置')
    aiForm.apiKey = ''
    ElMessage.success('AI 配置已保存')
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    aiSaving.value = false
  }
}

async function handleAiTest() {
  // 测试前会先持久化配置，明确告知用户并确认
  try {
    await ElMessageBox.confirm(
      '测试连接会先将当前 AI 配置（含 API Key）保存到服务器，确认继续？',
      '测试连接',
      { confirmButtonText: '继续', cancelButtonText: '取消', type: 'warning' }
    )
  } catch {
    return
  }
  aiTesting.value = true
  aiTestResult.value = ''
  try {
    await saveAiConfig({ apiKey: aiForm.apiKey || undefined, baseUrl: aiForm.baseUrl, model: aiForm.model })
    aiForm.apiKey = ''
    const r = await testAi()
    aiTestResult.value = r.message || (r.success ? '连接成功' : '连接失败')
    if (r.success) ElMessage.success(r.message)
    else ElMessage.error(r.message)
  } catch (err) {
    aiTestResult.value = err.message || '测试失败'
  } finally {
    aiTesting.value = false
  }
}

// ============ AI 识别导入 ============
async function handleAiFileChange(uploadFile) {
  const file = uploadFile?.raw
  if (!file) return
  aiParsing.value = true
  try {
    const isImage = (file.type || '').startsWith('image/')
    let payload
    if (isImage) {
      const img = await readImageAsBase64(file)
      payload = { kind: 'image', fileName: file.name, imageBase64: img.base64, imageMimeType: img.mime }
    } else {
      const rows = await readSheetRows(file)
      payload = { kind: 'sheet', fileName: file.name, rows }
    }
    const result = await aiParseRequirementDoc(payload)
    applyAiResult(result)
  } catch (err) {
    ElMessage.error('AI 识别失败：' + (err.message || err))
  } finally {
    aiParsing.value = false
  }
}

async function readSheetRows(file) {
  const XLSX = await import('xlsx')
  const data = await file.arrayBuffer()
  const wb = XLSX.read(data, { type: 'array' })
  const rows = []
  for (const sheetName of wb.SheetNames.slice(0, 4)) {
    if (rows.length > 0) rows.push([])
    rows.push(['工作表：' + sheetName])
    const sheetRows = XLSX.utils.sheet_to_json(wb.Sheets[sheetName], { header: 1, defval: '' })
    for (const r of sheetRows.slice(0, 400)) {
      rows.push(r.map(c => (c == null ? '' : String(c))))
    }
  }
  return rows.slice(0, 1500)
}

async function readImageAsBase64(file) {
  const dataUrl = await new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result)
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
  const img = await new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = reject
    image.src = dataUrl
  })
  const maxSide = 1600
  let width = img.width
  let height = img.height
  if (Math.max(width, height) > maxSide) {
    const scale = maxSide / Math.max(width, height)
    width = Math.round(width * scale)
    height = Math.round(height * scale)
  }
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext('2d')
  ctx.drawImage(img, 0, 0, width, height)
  const out = canvas.toDataURL('image/jpeg', 0.9)
  const idx = out.indexOf(',')
  return { base64: out.slice(idx + 1), mime: 'image/jpeg' }
}

function applyAiResult(result) {
  const entries = result?.entries || []
  if (entries.length === 0) {
    ElMessage.warning('AI 未识别出有效数据' + (result?.warnings?.length ? '（' + result.warnings.length + ' 条跳过）' : ''))
    return
  }
  const byType = { WORKDAY: 0, WEEKEND: 0, HOLIDAY: 0 }
  let skipped = 0
  const validWsIds = new Set(workstations.value.map(w => w.id))
  for (const e of entries) {
    const matrix = matrices.value[e.dayType]
    const slot = normalizeSlot(e.timeSlot)
    const wsId = e.workstationId
    if (!matrix || !slot || wsId == null || !validWsIds.has(wsId)) {
      skipped++
      continue
    }
    const row = matrix.find(r => r.slot === slot)
    if (!row) {
      skipped++
      continue
    }
    const prev = row.cells[wsId] || {}
    row.cells[wsId] = {
      min: e.requiredCount || 0,
      ideal: Math.max(e.idealCount || 0, e.requiredCount || 0),
      remark: prev.remark
    }
    byType[e.dayType]++
  }
  const summary = DAY_TYPES
    .filter(d => byType[d.value] > 0)
    .map(d => d.label + ' ' + byType[d.value] + ' 格')
    .join('、')
  ElMessage.success('AI 识别成功：' + summary + '（已并入编辑器，点击「保存」后生效）')
  if (skipped > 0) {
    ElMessage.warning(skipped + ' 条未匹配（日期类型/时段/工作站无效）已跳过')
  }
  if (result?.warnings?.length) {
    ElMessage.warning(result.warnings.length + ' 条跳过，前 3 条：' + result.warnings.slice(0, 3).join('；'))
  }
}

function applyParsed(out) {
  if (out.rows.length === 0) {
    ElMessage.warning(out.errors.length ? '没有解析到有效数据（' + out.errors.length + ' 处跳过）' : '没有解析到有效数据')
    return
  }

  const byType = { WORKDAY: 0, WEEKEND: 0, HOLIDAY: 0 }
  for (const row of out.rows) {
    const matrix = matrices.value[row.dayType]
    if (!matrix) continue
    const r = matrix.find(x => x.slot === row.slot)
    if (r) {
      // 导入无 remark 时保留原备注
      const prev = r.cells[row.workstationId] || {}
      r.cells[row.workstationId] = { min: row.min, ideal: row.ideal, remark: prev.remark }
      byType[row.dayType]++
    }
  }

  const summary = DAY_TYPES
    .filter(d => byType[d.value] > 0)
    .map(d => d.label + ' ' + byType[d.value] + ' 格')
    .join('、')
  ElMessage.success('导入成功：' + summary + '（已并入编辑器，点击「保存」后生效）')

  if (out.errors.length) {
    ElMessage.warning(out.errors.length + ' 处无法识别已跳过，前 3 条：' + out.errors.slice(0, 3).join('；'))
  }
}

onMounted(() => {
  loadData()
  window.addEventListener('mouseup', finishDrag)
  window.addEventListener('keydown', onKeydown)
  window.addEventListener('mousedown', onGlobalMouseDown)
})

onBeforeUnmount(() => {
  window.removeEventListener('mouseup', finishDrag)
  window.removeEventListener('keydown', onKeydown)
  window.removeEventListener('mousedown', onGlobalMouseDown)
})
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stats-bar {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}
.stat-box {
  flex: 1;
  min-width: 150px;
  background: #f7f9fc;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  padding: 10px 14px;
}
.stat-box.total {
  background: #ecf5ff;
  border-color: #d9ecff;
}
.stat-label {
  font-size: 12px;
  color: #909399;
  margin-bottom: 4px;
}
.stat-value {
  font-size: 22px;
  font-weight: 700;
  color: #303133;
}
.stat-unit {
  font-size: 12px;
  font-weight: 400;
  color: #606266;
}
.stat-sub {
  font-size: 11px;
  color: #909399;
  margin-top: 2px;
}

.gantt-wrap {
  overflow: auto;
  max-height: calc(100vh - 310px);
  min-height: 260px;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
}
.gantt-inner {
  min-width: max-content;
  user-select: none;
}
.gantt-header {
  display: flex;
  background: #f5f7fa;
  border-bottom: 1px solid #dcdfe6;
  position: sticky;
  top: 0;
  z-index: 3;
}
.gantt-corner {
  width: 80px;
  min-width: 80px;
  padding: 8px 6px;
  font-weight: 600;
  font-size: 12px;
  color: #606266;
  border-right: 1px solid #dcdfe6;
}
.gantt-axis {
  display: flex;
}
.axis-cell {
  width: 40px;
  min-width: 40px;
  height: 30px;
  line-height: 30px;
  text-align: center;
  font-size: 10px;
  color: #909399;
  border-right: 1px solid #ebeef5;
}
.axis-cell.hour {
  color: #303133;
  font-weight: 600;
  border-right-color: #dcdfe6;
}
.axis-cell.midnight {
  border-left: 2px solid #8fa8ff;
  color: #4c6fff;
}
.axis-cell.open-time {
  border-left: 2px solid #f59e0b;
  color: #d97706;
  font-weight: 700;
}

.gantt-row {
  display: flex;
  border-bottom: 1px solid #ebeef5;
}
.gantt-row:last-child {
  border-bottom: none;
}
.gantt-label {
  width: 80px;
  min-width: 80px;
  padding: 0 6px;
  display: flex;
  align-items: center;
  font-size: 12px;
  color: #303133;
  background: #fff;
  border-right: 1px solid #dcdfe6;
  position: sticky;
  left: 0;
  z-index: 2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.gantt-cells {
  display: flex;
}
.gantt-cell {
  width: 40px;
  min-width: 40px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 10px;
  color: #303133;
  border-right: 1px solid #f0f2f5;
  cursor: pointer;
  position: relative;
}
.gantt-cell:hover {
  outline: 2px solid #409eff;
  outline-offset: -2px;
}
.gantt-cell.midnight {
  border-left: 2px solid #8fa8ff;
}
.gantt-cell.open-time {
  border-left: 2px solid #f59e0b;
}
.gantt-cell.selected::after {
  content: '';
  position: absolute;
  inset: 0;
  background: rgba(64, 158, 255, 0.35);
  pointer-events: none;
}
.gantt-cell.has-remark::before {
  content: '';
  position: absolute;
  top: 3px;
  right: 3px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #e6a23c;
  pointer-events: none;
}
.lv0 {
  background: #f5f7fa;
}
.lv1 {
  background: #d9ead3;
}
.lv2 {
  background: #b6d7a8;
}
.lv3 {
  background: #93c47d;
}
.lv4 {
  background: #6aa84f;
  color: #fff;
}
.gantt-cell.soft {
  background-image: repeating-linear-gradient(135deg, rgba(0, 0, 0, 0.08) 0 4px, transparent 4px 8px);
}

.legend {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 10px;
  border-top: 1px solid #dcdfe6;
  flex-wrap: wrap;
}
.legend-title {
  font-size: 12px;
  color: #606266;
}
.sw {
  width: 30px;
  height: 20px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 10px;
  color: #303133;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
}

.cell-tip {
  position: fixed;
  z-index: 3200;
  max-width: 240px;
  background: rgba(31, 45, 61, 0.95);
  color: #fff;
  font-size: 12px;
  border-radius: 6px;
  padding: 8px 10px;
  pointer-events: none;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
  line-height: 1.5;
}
.tip-title {
  font-weight: 600;
  margin-bottom: 2px;
}
.tip-demand {
  color: #d9ead3;
}
.tip-remark {
  color: #f6c56e;
  margin-top: 2px;
}

.cell-editor {
  position: fixed;
  z-index: 3000;
  width: 220px;
  background: #fff;
  border: 1px solid #dcdfe6;
  border-radius: 6px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.15);
  padding: 12px;
}
.editor-title {
  font-size: 13px;
  font-weight: 600;
  color: #303133;
  margin-bottom: 10px;
}
.editor-row {
  display: flex;
  align-items: center;
  margin-bottom: 8px;
}
.editor-label {
  width: 44px;
  font-size: 12px;
  color: #606266;
}
.editor-quick {
  display: flex;
  gap: 6px;
  margin-bottom: 10px;
}
.editor-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}
</style>
