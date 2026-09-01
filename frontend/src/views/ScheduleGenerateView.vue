<template>
  <div>
    <el-card>
      <template #header>一键生成排班</template>
      <el-form label-width="110px" style="max-width: 560px">
        <el-form-item label="排班方式" required>
          <el-radio-group v-model="scheduleMode">
            <el-radio value="week">未来一周</el-radio>
            <el-radio value="currentMonth">本月（1号-月末）</el-radio>
            <el-radio value="nextMonth">下月（1号-月末）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="参考日期" required>
          <el-date-picker class="u-w-full" v-model="refDate" type="date" value-format="YYYY-MM-DD" />
        </el-form-item>
        <el-form-item label="排班周期">
          <span style="font-size: var(--app-font-md); color: var(--el-text-color-regular)">{{ startDate }} ~ {{ endDate }}（{{ rangeDays }} 天）</span>
        </el-form-item>
        <el-form-item label="计划名称">
          <el-input v-model="planName" placeholder="留空自动生成" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="generating" :disabled="generating" @click="handleGenerate">生成排班</el-button>
        </el-form-item>
      </el-form>

      <el-divider content-position="left">周期需求概览（营业日 12:00-次日 06:00 口径）</el-divider>
      <el-table :data="previewRows" v-loading="previewLoading" border size="small" style="max-width: 720px">
        <el-table-column prop="label" label="日期类型" width="110" />
        <el-table-column prop="days" label="天数" width="70" align="center" />
        <el-table-column prop="minHours" label="最少需求（人·时）" width="150" align="center" />
        <el-table-column prop="idealHours" label="最好需求（人·时）" width="150" align="center" />
        <el-table-column label="峰值并发（最少/最好）" width="180" align="center">
          <template #default="{ row }">{{ row.peakMin }} / {{ row.peakIdeal }} 人</template>
        </el-table-column>
      </el-table>

      <el-alert class="u-mt-6"
        v-if="result"
        type="success"
        :closable="false"
       
        :title="'生成完成：' + result.planName"
      >
        <div class="u-mt-4">
          <el-tag class="u-mr-4">休息日 {{ result.restDayCount }} 条</el-tag>
          <el-tag class="u-mr-4" type="info">班次分配 {{ result.shiftAssignmentCount }} 条</el-tag>
          <el-tag class="u-mr-4" type="info">工作站 {{ result.workstationAssignmentCount }} 条</el-tag>
          <el-tag class="u-mr-4" type="warning">问题 {{ result.issueCount }} 条</el-tag>
          <el-tag class="u-mr-4" v-if="result.demandShiftCount > 0" type="primary">按需补班 {{ result.demandShiftCount }} 个</el-tag>
        </div>
        <div class="u-mt-5" style="font-size: var(--app-font-base); color: var(--el-text-color-regular)">
          需求覆盖：最少 {{ result.demandMinHours }} 人·时 / 最好 {{ result.demandIdealHours }} 人·时 ｜
          已覆盖 {{ result.coveredHours }} 人·时 ｜ 缺口 {{ result.gapHours }} 人·时 ｜
          覆盖率 {{ result.coveragePct }}%
        </div>
        <div class="u-mt-5">
          <el-button type="primary" size="small" @click="goToPlan(result.planId)">查看排班计划</el-button>
        </div>
      </el-alert>
    </el-card>

    <el-card class="u-mt-6">
      <template #header>历史排班计划</template>
      <el-table :data="plans" v-loading="plansLoading" border stripe>
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="planName" label="计划名称" />
        <el-table-column label="周期" width="220">
          <template #default="{ row }">{{ row.startDate }} ~ {{ row.endDate }}</template>
        </el-table-column>
        <el-table-column prop="employeeCount" label="员工数" width="80" />
        <el-table-column prop="issueCount" label="问题数" width="80" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.status === 'PUBLISHED' ? 'success' : 'info'">{{ row.status === 'PUBLISHED' ? '已发布' : '草稿' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="320">
          <template #default="{ row }">
            <el-button link type="primary" @click="goView(row.id)">查看排班</el-button>
            <el-button v-if="row.status === 'DRAFT'" link type="success" @click="handlePublish(row)">发布</el-button>
            <el-button v-if="row.status === 'PUBLISHED'" link type="danger" @click="handleUnpublish(row)">取消发布</el-button>
            <el-button v-if="row.status === 'DRAFT'" link type="primary" @click="handleCopyPrevious(row)">复制上周</el-button>
            <el-button link type="warning" @click="openAdjustments(row)">调整记录</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination class="u-mt-6"
       
        layout="total, prev, pager, next"
        :total="plansTotal"
        :page-size="pageSize"
        :current-page="page"
        @current-change="loadPlans"
      />
    </el-card>

    <!-- 调整记录明细（店长修改全程记录） -->
    <el-dialog v-model="adjustDialog.visible" :title="'调整记录 - ' + adjustDialog.planName" width="860px" destroy-on-close>
      <el-table :data="adjustDialog.items" size="small" max-height="420">
        <el-table-column label="时间" width="150">
          <template #default="{ row }">{{ formatAdjTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="110">
          <template #default="{ row }">
            <el-tag :type="actionTagType(row.actionType)" size="small">{{ actionLabel(row.actionType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="employeeNo" label="工号" width="80" />
        <el-table-column prop="employeeName" label="员工" width="90" />
        <el-table-column label="日期/时段" width="140">
          <template #default="{ row }">{{ row.workDate ? String(row.workDate).slice(0, 10) : '-' }}{{ row.timeSlot ? ' ' + String(row.timeSlot).slice(0, 5) : '' }}</template>
        </el-table-column>
        <el-table-column label="调整内容" min-width="200">
          <template #default="{ row }">{{ describeAdjustment(row) }}</template>
        </el-table-column>
        <el-table-column prop="operatorName" label="操作人" width="100" />
      </el-table>
      <template #footer>
        <el-pagination
          small
          layout="total, prev, pager, next"
          :total="adjustDialog.total"
          :page-size="adjustDialog.pageSize"
          :current-page="adjustDialog.page"
          @current-change="loadAdjustments"
        />
        <el-button @click="adjustDialog.visible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { generateSchedule, getSchedules, publishSchedule, unpublishSchedule, deleteSchedule, getScheduleIssues, getAdjustmentSummary, getScheduleAdjustments, copyPreviousWeek, getDemandInsights } from '../api/schedules'
import { getStaffingRequirementPreview } from '../api/staffingRequirements'
import { getWorkstations } from '../api/workstations'
import { getShiftTemplates } from '../api/shiftTemplates'

function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const router = useRouter()
const scheduleMode = ref('week')
const refDate = ref(getToday())
const planName = ref('')
const generating = ref(false)
const result = ref(null)

const plans = ref([])
const plansTotal = ref(0)
const plansLoading = ref(false)
const page = ref(1)
const pageSize = 10

// 周期需求概览
const previewRows = ref([])
const previewLoading = ref(false)
const DAY_TYPE_LABELS = { WORKDAY: '平日', WEEKEND: '周末', HOLIDAY: '节假日' }

async function loadPreview() {
  if (!startDate.value || !endDate.value) {
    previewRows.value = []
    return
  }
  previewLoading.value = true
  try {
    const data = await getStaffingRequirementPreview({ startDate: startDate.value, endDate: endDate.value })
    const rows = Object.entries(data.byType || {})
      // 周期内没有该日期类型（如无节假日）时不展示该行，避免出现一排 0
      .filter(([, v]) => (v.days || 0) > 0)
      .map(([type, v]) => ({
        label: DAY_TYPE_LABELS[type] || type,
        days: v.days,
        minHours: v.minHours,
        idealHours: v.idealHours,
        peakMin: v.peakMin,
        peakIdeal: v.peakIdeal
      }))
    rows.push({
      label: '合计',
      days: '-',
      minHours: data.totalMinHours,
      idealHours: data.totalIdealHours,
      peakMin: '-',
      peakIdeal: '-'
    })
    previewRows.value = rows
  } catch {
    previewRows.value = []
  } finally {
    previewLoading.value = false
  }
}

// 根据排班方式 + 参考日期计算起止
function computeRange() {
    if (!refDate.value) return null
    const d = new Date(refDate.value + 'T00:00:00')
    if (Number.isNaN(d.getTime())) return null
  const fmt = x => `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`

  if (scheduleMode.value === 'week') {
    const start = new Date(d)
    const end = new Date(d)
    end.setDate(d.getDate() + 6)
    return { start: fmt(start), end: fmt(end) }
  }

  // 本月/下月：1号 ~ 月末
  let year = d.getFullYear()
  let month = d.getMonth() // 0-based
  if (scheduleMode.value === 'nextMonth') {
    month += 1
    if (month > 11) { month = 0; year += 1 }
  }
  const start = new Date(year, month, 1)
  const end = new Date(year, month + 1, 0) // 下月0号 = 本月最后一天
  return { start: fmt(start), end: fmt(end) }
}

const startDate = ref('')
const endDate = ref('')
const rangeDays = computed(() => {
  if (!startDate.value || !endDate.value) return 0
  const s = new Date(startDate.value + 'T00:00:00')
  const e = new Date(endDate.value + 'T00:00:00')
  return Math.round((e - s) / 86400000) + 1
})

// P3-6: 处理 refDate 清空/无效的情况
function refreshRange() {
  const r = computeRange()
  if (!r) {
    startDate.value = ''
    endDate.value = ''
    return
  }
  startDate.value = r.start
  endDate.value = r.end
}

watch([scheduleMode, refDate], refreshRange, { immediate: true })
// 修复：watch2 需 immediate——否则 watch1(immediate) 同步执行 refreshRange 更新日期后，
// watch2 才注册并收集依赖（初始值已是更新后的日期），首次进入页面概览永远不加载。
watch([startDate, endDate], loadPreview, { immediate: true })

async function handleGenerate() {
  if (generating.value) return
  if (!startDate.value || !endDate.value) {
    ElMessage.warning('请先选择排班方式，系统会自动计算日期范围')
    return
  }
  generating.value = true
  try {
    result.value = await generateSchedule({
      startDate: startDate.value,
      endDate: endDate.value,
      planName: planName.value || undefined
    })
    ElMessage.success('排班生成成功')
    loadPlans(1)
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    generating.value = false
  }
}

function goToPlan(id) { goView(id) }
function goView(id) { router.push({ path: '/schedules/view', query: { planId: id } }) }

async function loadPlans(current = 1) {
  page.value = current
  plansLoading.value = true
  try {
    const res = await getSchedules({ page: page.value, pageSize })
    plans.value = res.items
    plansTotal.value = res.total
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    plansLoading.value = false
  }
}

// ===== 调整记录明细（店长修改全程记录） =====
const adjustDialog = reactive({
  visible: false,
  planId: null,
  planName: '',
  items: [],
  total: 0,
  page: 1,
  pageSize: 50,
  loading: false
})

const ADJUST_LABELS = {
  MOVE_SEGMENT: '移动工作段',
  SET_REST: '改为休息',
  SET_WORK: '恢复上班',
  ADD_SLOT: '空位加人',
  REMOVE_SLOT: '撤回加人',
  MOVE_RANGE: '范围平移',
  REPLACE_SLOT: '范围换人',
  CLEAR_RANGE: '取消排班',
  COPY_PREVIOUS: '复制上周',
  ADJUST: '手动调整'
}
const ADJUST_TAG_TYPES = {
  MOVE_SEGMENT: 'warning',
  SET_REST: 'danger',
  SET_WORK: 'success',
  ADD_SLOT: 'success',
  REMOVE_SLOT: 'danger',
  MOVE_RANGE: 'warning',
  REPLACE_SLOT: 'warning',
  CLEAR_RANGE: 'danger',
  COPY_PREVIOUS: 'info',
  ADJUST: 'info'
}
const actionLabel = (t) => ADJUST_LABELS[t] || t
const actionTagType = (t) => ADJUST_TAG_TYPES[t] || 'info'

function formatAdjTime(t) {
  if (!t) return ''
  return String(t).replace('T', ' ').slice(0, 19)
}

// 工作站 / 班次名称映射（打开调整记录时加载，用于把 JSON 里的 ID 翻译成人话）
const wsMap = ref({})
const shiftMap = ref({})
async function ensureDicts() {
  if (Object.keys(wsMap.value).length === 0) {
    try {
      const ws = await getWorkstations({ status: 1 })
      wsMap.value = Object.fromEntries((ws || []).map(x => [x.id, x.name]))
      const shifts = await getShiftTemplates()
      shiftMap.value = Object.fromEntries((shifts || []).map(x => [x.id, x.code || x.name]))
    } catch { /* 字典加载失败时退回显示 ID */ }
  }
}
const wsName = (id) => (id != null && wsMap.value[id]) || (id != null ? `站${id}` : '')
const shiftCode = (id) => (id != null && shiftMap.value[id]) || (id != null ? `班次${id}` : '')

// 半小时段数 → 结束时间（timeSlot 为起点）
function endOfSlots(start, slotCount) {
  if (!start || !slotCount) return ''
  const [h, m] = String(start).split(':').map(Number)
  const total = h * 60 + m + 30 * slotCount
  const hh = String(Math.floor(total / 60) % 24).padStart(2, '0')
  const mm = String(total % 60).padStart(2, '0')
  return `${hh}:${mm}`
}

function describeAdjustment(row) {
  try {
    const before = row.beforeJson ? JSON.parse(row.beforeJson) : null
    const after = row.afterJson ? JSON.parse(row.afterJson) : null
    const emp = row.employeeName || ''
    const ts = row.timeSlot ? String(row.timeSlot).slice(0, 5) : ''

    switch (row.actionType) {
      case 'MOVE_SEGMENT': {
        // 移动工作段：从 站A 16:00 → 站B 14:30（换站/换时间）
        const b = before || {}
        const a = after || {}
        const bWs = wsName(b.WorkstationId)
        const aWs = wsName(a.WorkstationId)
        const bT = (b.TimeSlot || '').slice(0, 5)
        const aT = (a.TimeSlot || '').slice(0, 5)
        if (bWs === aWs) return `${emp} 在${aWs}的工作段 ${bT} → ${aT}`
        return `${emp} 工作段：${bWs} ${bT} → ${aWs} ${aT}`
      }
      case 'SET_REST':
        return `${emp} ${ts} 起改为休息（半小时）`
      case 'SET_WORK':
        return `${emp} ${ts} 恢复上班`
      case 'ADD_SLOT': {
        const a = after || {}
        const start = (a.StartTime || ts || '').slice(0, 5)
        const end = (a.EndTime || endOfSlots(start, a.SlotCount) || '').slice(0, 5)
        return `${emp} 空位加人：${wsName(a.WorkstationId)} ${start}-${end}（${a.SlotCount ?? '?'} 段）`
      }
      case 'REMOVE_SLOT': {
        const b = before || {}
        return `${emp} 撤回加人：${wsName(b.WorkstationId)} ${ts} 起 ${b.SlotCount ?? '?'} 段`
      }
      case 'MOVE_RANGE': {
        const b = before || {}
        const a = after || {}
        const slots = (b.TimeSlots || []).map(s => String(s).slice(0, 5))
        const range = slots.length ? `${slots[0]}-${slots[slots.length - 1]}` : ''
        const dir = a.OffsetMinutes > 0 ? `右移 ${a.OffsetMinutes} 分钟` : `左移 ${-a.OffsetMinutes} 分钟`
        return `${wsName(b.WorkstationId)} ${range} 共 ${a.MovedRows ?? slots.length} 条明细，整体${dir}`
      }
      case 'REPLACE_SLOT': {
        const b = before || {}
        const a = after || {}
        const removed = (b.RemovedEmployeeIds || []).length
        return `${wsName(a.WorkstationId)} ${ts} 起 ${a.SlotCount ?? '?'} 段：原 ${removed} 人 → 换成 ${emp}`
      }
      case 'CLEAR_RANGE': {
        const b = before || {}
        const slots = (b.TimeSlots || []).map(s => String(s).slice(0, 5))
        const range = slots.length ? `${slots[0]}-${slots[slots.length - 1]}` : ts
        return `取消排班：${wsName(b.WorkstationId)} ${range}（${(b.AffectedEmployeeIds || []).length} 人受影响）`
      }
      case 'COPY_PREVIOUS': {
        const a = after || {}
        return `从「${a.SourcePlanName || '上周排班'}」复制，按星期几对齐，共 ${a.CopiedDays ?? '?'} 条`
      }
      case 'ADJUST': {
        const a = after || {}
        const shift = shiftCode(a.ShiftTemplateId)
        const ws = wsName(a.WorkstationId)
        const parts = [shift, ws].filter(Boolean)
        return `${emp} ${ts}：${parts.join(' · ') || '调整安排'}`
      }
      default:
        // 未知类型：尝试显示 afterJson 中的关键字段，避免直接抛原始 JSON
        if (after && typeof after === 'object') {
          const keys = Object.keys(after).filter(k => !['EmployeeId', 'WorkDate', 'TimeSlot'].includes(k))
          return keys.map(k => `${k}=${typeof after[k] === 'object' ? JSON.stringify(after[k]) : after[k]}`).join('，') || row.afterJson || ''
        }
        return row.afterJson || ''
    }
  } catch {
    return row.afterJson || ''
  }
}

async function loadAdjustments(page) {
  if (!adjustDialog.planId) return
  adjustDialog.loading = true
  try {
    const res = await getScheduleAdjustments(adjustDialog.planId, page, adjustDialog.pageSize)
    adjustDialog.page = page
    adjustDialog.items = res.items || []
    adjustDialog.total = res.total || 0
  } catch (e) {
    ElMessage.error('加载调整记录失败：' + (e.message || '网络错误'))
  } finally {
    adjustDialog.loading = false
  }
}

function openAdjustments(row) {
  adjustDialog.planId = row.id
  adjustDialog.planName = row.planName
  adjustDialog.visible = true
  ensureDicts()
  loadAdjustments(1)
}

// P2：复制上周（以最近一期已发布排班为起点，按星期几对齐）
async function handleCopyPrevious(row) {
  try {
    await ElMessageBox.confirm('将以最近一期已发布的排班为模板复制到「' + row.planName + '」（覆盖当前草稿内容，按星期几对齐）。确定继续吗？', '复制上周', { type: 'info' })
  } catch {
    return
  }
  try {
    await copyPreviousWeek(row.id)
    ElMessage.success('已复制上周排班，可在排班查看中微调')
    loadPlans(page.value)
  } catch (e) {
    ElMessage.error('复制失败：' + (e.message || '网络错误'))
  }
}

async function handlePublish(row) {
  // P0 交互：发布前拉取调整摘要（生成快照 vs 当前），让店长知道系统将学习什么
  let summaryText = ''
  try {
    const summary = await getAdjustmentSummary(row.id)
    if (summary?.totalAdjustments > 0) {
      summaryText = '\n\n本期手动调整 ' + summary.totalAdjustments + ' 处（改休 ' + summary.restChanges + ' / 换班 ' + summary.shiftChanges + '），发布后系统将学习这些调整。'
    }
  } catch (e) {
    // 摘要获取失败不阻断发布（旧计划无快照时摘要为 0）
  }

  // P1 交互：需求联动建议（店长反复手动补人的时段 → 提示调整人数需求）
  let insightText = ''
  try {
    const insights = await getDemandInsights(row.id)
    if (Array.isArray(insights) && insights.length > 0) {
      insightText = '\n\n需求联动建议（该时段被多次手动补人，可能人数需求配置不足）：\n' +
        insights.slice(0, 3).map(i =>
          '  · ' + dayTypeLabel(i.dayType) + ' ' + i.timeSlot + ' ' + i.workstationCode +
          '（补人 ' + i.signalCount + ' 次，当前需求 ' + i.currentRequired + '，建议 ' + i.suggestedRequired + '）'
        ).join('\n')
    }
  } catch (e) {
    // 建议获取失败不阻断发布
  }

  try {
    await ElMessageBox.confirm('确定发布排班 ' + row.planName + ' 吗？' + summaryText + insightText, '提示', { type: 'warning' })
  } catch {
    return
  }

  // 检查排班是否存在 ERROR 严重问题（如真实高技能岗位缺口）
  let hasError = false
  try {
    const issues = await getScheduleIssues(row.id)
    const list = Array.isArray(issues) ? issues : (issues?.items || [])
    hasError = list.some(i => i.severity === 'ERROR')
  } catch (e) {
    // 区分"检查失败"与"无错误"：无法确认时中止发布，避免 fail-open
    ElMessage.error('问题检查失败，已中止发布：' + (e?.message || '网络错误'))
    return
  }

  if (hasError) {
    // 有 ERROR：弹二次确认，用户可选择继续发布（force=true）
    try {
      await ElMessageBox.confirm(
        '该排班计划存在严重违规（ERROR）问题，直接发布可能存在岗位缺口风险。\n\n确认继续发布吗？',
        '发布确认',
        { type: 'error', confirmButtonText: '继续发布', cancelButtonText: '取消' }
      )
      await publishSchedule(row.id, true)
    } catch (e) {
      return // 用户取消强制发布
    }
  } else {
    try {
      await publishSchedule(row.id, false)
    } catch (e) {
      /* 拦截器已提示 */
      return
    }
  }

  ElMessage.success('发布成功')
  loadPlans(page.value)
}

// 取消发布：已发布排班退回草稿（员工端班表消失，可调整后重新发布）
async function handleUnpublish(row) {
  try {
    await ElMessageBox.confirm(
      '确定取消发布「' + row.planName + '」吗？取消后员工端将不再显示该班表，计划退回草稿，可继续调整后重新发布。',
      '取消发布',
      { type: 'warning', confirmButtonText: '取消发布', cancelButtonText: '再想想' }
    )
  } catch {
    return
  }
  try {
    await unpublishSchedule(row.id)
    ElMessage.success('已取消发布，排班退回草稿')
    loadPlans(page.value)
  } catch (e) {
    /* 拦截器已提示 */
  }
}

async function handleDelete(row) {
  try {
    await ElMessageBox.confirm('确定删除排班计划「' + row.planName + '」吗？删除后明细、汇总、问题将一并移除，且不可恢复。', '删除确认', { type: 'warning' })
  } catch {
    return
  }
  try {
    await deleteSchedule(row.id)
    ElMessage.success('删除成功')
    // 删除当前页最后一条时回退到上一页
    if (plans.value.length === 1 && page.value > 1) {
      loadPlans(page.value - 1)
    } else {
      loadPlans(page.value)
    }
  } catch (e) {
    /* 拦截器已提示 */
  }
}

onMounted(() => loadPlans(1))
</script>
