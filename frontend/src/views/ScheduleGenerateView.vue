<template>
  <div>
    <el-card>
      <template #header>一键生成排班</template>
      <el-form label-width="110px" style="max-width: 560px">
        <el-form-item label="排班方式" required>
          <el-radio-group v-model="scheduleMode">
            <el-radio label="week">未来一周</el-radio>
            <el-radio label="currentMonth">本月（1号-月末）</el-radio>
            <el-radio label="nextMonth">下月（1号-月末）</el-radio>
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
          <el-button type="primary" :loading="generating" @click="handleGenerate">生成排班</el-button>
        </el-form-item>
      </el-form>

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
import { generateSchedule, getSchedules, publishSchedule, deleteSchedule } from '../api/schedules'

const router = useRouter()
const scheduleMode = ref('week')
const refDate = ref('2026-08-01')
const planName = ref('')
const generating = ref(false)
const result = ref(null)

const plans = ref([])
const plansTotal = ref(0)
const plansLoading = ref(false)
const page = ref(1)
const pageSize = 10

// 根据排班方式 + 参考日期计算起止
function computeRange() {
  const d = new Date(refDate.value + 'T00:00:00')
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

function refreshRange() {
  const r = computeRange()
  startDate.value = r.start
  endDate.value = r.end
}

watch([scheduleMode, refDate], refreshRange, { immediate: true })

async function handleGenerate() {
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
  } finally {
    plansLoading.value = false
  }
}

async function handlePublish(row) {
  await ElMessageBox.confirm('确定发布排班 ' + row.planName + ' 吗？', '提示', { type: 'warning' })
  await publishSchedule(row.id)
  ElMessage.success('发布成功')
  loadPlans(page.value)
}

async function handleDelete(row) {
  await ElMessageBox.confirm('确定删除排班计划「' + row.planName + '」吗？删除后明细、汇总、问题将一并移除，且不可恢复。', '删除确认', { type: 'warning' })
  await deleteSchedule(row.id)
  ElMessage.success('删除成功')
  loadPlans(page.value)
}

onMounted(() => loadPlans(1))
</script>
