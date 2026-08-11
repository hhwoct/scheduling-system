<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>排班报表</span>
          <div style="display: flex; gap: 8px">
            <el-button v-if="planId" type="success" :loading="exporting" @click="exportCsv">导出 CSV</el-button>
            <el-button type="primary" :loading="loading" @click="loadReport">查询</el-button>
          </div>
        </div>
      </template>

      <!-- 排班计划选择 -->
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

      <template v-if="summary">
        <!-- 摘要卡片 -->
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="4"><el-card shadow="hover"><div class="stat">{{ summary.employeeCount || 0 }}</div><div class="label">员工数</div></el-card></el-col>
          <el-col :span="4"><el-card shadow="hover"><div class="stat">{{ summary.restDayCount || 0 }}</div><div class="label">休息人天</div></el-card></el-col>
          <el-col :span="4"><el-card shadow="hover"><div class="stat">{{ summary.workDayCount || 0 }}</div><div class="label">上班人天</div></el-card></el-col>
          <el-col :span="4"><el-card shadow="hover"><div class="stat">{{ summary.totalWorkHours || 0 }}</div><div class="label">总工时</div></el-card></el-col>
          <el-col :span="4"><el-card shadow="hover"><div class="stat" style="color:#e6a23c">{{ summary.issueCount || 0 }}</div><div class="label">问题数</div></el-card></el-col>
          <el-col :span="4"><el-card shadow="hover"><div class="stat" :style="{ color: (summary.gapCount || 0) > 0 ? '#f56c6c' : '#67c23a' }">{{ summary.gapCount || 0 }}</div><div class="label">岗位缺口</div></el-card></el-col>
        </el-row>

        <!-- 员工工时汇总 -->
        <el-card header="员工工时汇总" style="margin-bottom: 16px">
          <el-table :data="employeeStats" border stripe size="small" max-height="400">
            <el-table-column prop="employeeNo" label="工号" width="100" />
            <el-table-column prop="employeeName" label="姓名" width="120" />
            <el-table-column prop="department" label="部门" width="100" />
            <el-table-column label="休息天数" width="90">
              <template #default="{ row }">{{ row.restCount }}</template>
            </el-table-column>
            <el-table-column label="上班天数" width="90">
              <template #default="{ row }">{{ row.workCount }}</template>
            </el-table-column>
            <el-table-column label="总工时" width="100" prop="totalHours" />
            <el-table-column label="常上班次" width="120">
              <template #default="{ row }">{{ row.topShift || '--' }}</template>
            </el-table-column>
          </el-table>
        </el-card>

        <!-- 逐日明细 -->
        <el-card header="逐日明细" style="margin-bottom: 16px">
          <el-table :data="pagedRows" border stripe size="small" max-height="400">
            <el-table-column prop="employeeNo" label="工号" width="100" />
            <el-table-column prop="employeeName" label="姓名" width="120" />
            <el-table-column prop="department" label="部门" width="100" />
            <el-table-column prop="workDate" label="日期" width="120" />
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag v-if="row.isRestDay === 1" type="danger" size="small">休息</el-tag>
                <el-tag v-else type="success" size="small">上班</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="shiftCode" label="班次" width="80" />
            <el-table-column prop="workHours" label="工时" width="80" />
          </el-table>
          <el-pagination
            style="margin-top: 12px"
            layout="total, prev, pager, next"
            :total="detailTotal"
            :page-size="detailPageSize"
            :current-page="detailPage"
            @current-change="detailPage = $event"
          />
        </el-card>
      </template>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { getMonthView, getScheduleSummary, getSchedules } from '../api/schedules'

const planId = ref('')
const loading = ref(false)
const exporting = ref(false)
const plans = ref([])
const plansLoading = ref(false)
const summary = ref(null)
const rows = ref([])
const detailPage = ref(1)
const detailPageSize = 20

const pagedRows = computed(() => {
  const start = (detailPage.value - 1) * detailPageSize
  return rows.value.slice(start, start + detailPageSize)
})
const detailTotal = computed(() => rows.value.length)

const employeeStats = computed(() => {
  const map = {}
  rows.value.forEach(r => {
    if (!map[r.employeeNo]) {
      map[r.employeeNo] = { employeeNo: r.employeeNo, employeeName: r.employeeName, department: r.department, restCount: 0, workCount: 0, totalHours: 0, shiftCounts: {} }
    }
    const entry = map[r.employeeNo]
    if (r.isRestDay === 1) entry.restCount++
    else {
      entry.workCount++
      entry.totalHours += Number(r.workHours) || 0
      if (r.shiftCode) entry.shiftCounts[r.shiftCode] = (entry.shiftCounts[r.shiftCode] || 0) + 1
    }
  })
  return Object.values(map).map(e => {
    const top = Object.entries(e.shiftCounts).sort((a, b) => b[1] - a[1])[0]
    return { ...e, totalHours: e.totalHours.toFixed(1), topShift: top ? `${top[0]}(${top[1]})` : null }
  })
})

function selectPlan(row) {
  if (!row) return
  planId.value = row.id
  loadReport()
}

async function loadPlans() {
  plansLoading.value = true
  try {
    const res = await getSchedules({ page: 1, pageSize: 100, status: 'PUBLISHED' })
    plans.value = res.items || []
  } finally {
    plansLoading.value = false
  }
}

async function loadReport() {
  if (!planId.value) return
  loading.value = true
  try {
    const [s, m] = await Promise.all([
      getScheduleSummary(planId.value),
      getMonthView(planId.value)
    ])
    summary.value = s
    rows.value = (m || []).flatMap(r =>
      (r.days || []).map(d => ({
        employeeNo: r.employeeNo,
        employeeName: r.employeeName,
        department: r.department,
        workDate: d.workDate,
        isRestDay: d.isRestDay,
        shiftCode: d.shiftCode,
        workHours: d.workHours
      }))
    )
    detailPage.value = 1
  } finally {
    loading.value = false
  }
}

// CSV 注入防护：转义逗号/引号/换行，并中性化 Excel 公式前缀(=, +, -, @)
function csvEscape(value) {
  let v = String(value ?? '')
  // 公式注入防护：若以 =, +, -, @, \t, \r 开头，在前面加单引号
  if (/^[=+\-@\t\r]/.test(v)) {
    v = "'" + v
  }
  // 特殊字符转义：包含逗号/引号/换行时用双引号包裹，内部引号翻倍
  if (/[",\n\r]/.test(v)) {
    v = '"' + v.replace(/"/g, '""') + '"'
  }
  return v
}

async function exportCsv() {
  exporting.value = true
  try {
    const headers = ['工号', '姓名', '部门', '日期', '状态', '班次', '工时'].map(csvEscape).join(',')
    const csv = [headers]
    rows.value.forEach(r => {
      csv.push([
        csvEscape(r.employeeNo),
        csvEscape(r.employeeName),
        csvEscape(r.department),
        csvEscape(r.workDate),
        csvEscape(r.isRestDay === 1 ? '休息' : '上班'),
        csvEscape(r.shiftCode),
        csvEscape(r.workHours)
      ].join(','))
    })
    const blob = new Blob(['\uFEFF' + csv.join('\n')], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `排班报表_${planId.value}_${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success('导出成功')
  } finally {
    exporting.value = false
  }
}

onMounted(loadPlans)
</script>

<style scoped>
.stat { font-size: 26px; font-weight: 700; color: #409eff; }
.label { margin-top: 6px; color: #909399; }
</style>