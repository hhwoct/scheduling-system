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
          <el-date-picker v-model="refDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="排班周期">
          <span style="font-size: 14px; color: #606266">{{ startDate }} ~ {{ endDate }}（{{ rangeDays }} 天）</span>
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

      <el-alert
        v-if="result"
        type="success"
        :closable="false"
        style="margin-top: 16px"
        :title="'生成完成：' + result.planName"
      >
        <div style="margin-top: 8px">
          <el-tag style="margin-right: 8px">休息日 {{ result.restDayCount }} 条</el-tag>
          <el-tag type="info" style="margin-right: 8px">班次分配 {{ result.shiftAssignmentCount }} 条</el-tag>
          <el-tag type="info" style="margin-right: 8px">工作站 {{ result.workstationAssignmentCount }} 条</el-tag>
          <el-tag type="warning" style="margin-right: 8px">问题 {{ result.issueCount }} 条</el-tag>
          <el-tag v-if="result.demandShiftCount > 0" type="primary" style="margin-right: 8px">按需补班 {{ result.demandShiftCount }} 个</el-tag>
        </div>
        <div style="margin-top: 12px; font-size: 13px; color: #606266">
          需求覆盖：最少 {{ result.demandMinHours }} 人·时 / 最好 {{ result.demandIdealHours }} 人·时 ｜
          已覆盖 {{ result.coveredHours }} 人·时 ｜ 缺口 {{ result.gapHours }} 人·时 ｜
          覆盖率 {{ result.coveragePct }}%
        </div>
        <div style="margin-top: 12px">
          <el-button type="primary" size="small" @click="goToPlan(result.planId)">查看排班计划</el-button>
        </div>
      </el-alert>
    </el-card>

    <el-card style="margin-top: 16px">
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
        <el-table-column label="操作" width="260">
          <template #default="{ row }">
            <el-button link type="primary" @click="goView(row.id)">查看排班</el-button>
            <el-button v-if="row.status === 'DRAFT'" link type="success" @click="handlePublish(row)">发布</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        style="margin-top: 16px"
        layout="total, prev, pager, next"
        :total="plansTotal"
        :page-size="pageSize"
        :current-page="page"
        @current-change="loadPlans"
      />
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { generateSchedule, getSchedules, publishSchedule, deleteSchedule, getScheduleIssues } from '../api/schedules'
import { getStaffingRequirementPreview } from '../api/staffingRequirements'

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
    const rows = Object.entries(data.byType || {}).map(([type, v]) => ({
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
watch([startDate, endDate], loadPreview)

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

async function handlePublish(row) {
  try {
    await ElMessageBox.confirm('确定发布排班 ' + row.planName + ' 吗？', '提示', { type: 'warning' })
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
