<template>
  <div class="emp-wrap">
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>我的班表</span>
          <div>
            <el-date-picker
              v-model="month"
              type="month"
              value-format="YYYY-MM"
              style="width: 140px; margin-right: 8px"
            />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <!-- 员工信息 -->
      <el-descriptions v-if="employee" :column="4" border style="margin-bottom: 12px">
        <el-descriptions-item label="工号">{{ employee.employeeNo }}</el-descriptions-item>
        <el-descriptions-item label="姓名">{{ employee.name }}</el-descriptions-item>
        <el-descriptions-item label="部门">{{ employee.department }}</el-descriptions-item>
        <el-descriptions-item label="周期">{{ plans.length }} 个已发布计划</el-descriptions-item>
      </el-descriptions>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" />
      <el-empty v-else-if="!loading && plans.length === 0" description="本月暂无已发布的排班" />

      <!-- 每个已发布计划一张周表 -->
      <div v-for="plan in plans" :key="plan.id" style="margin-bottom: 20px">
        <div class="plan-title">{{ plan.planName }}（{{ plan.startDate }} ~ {{ plan.endDate }}）</div>
        <el-table :data="plan.days" border stripe size="small" max-height="260">
          <el-table-column label="日期" width="120">
            <template #default="{ row }">{{ row.workDate }}</template>
          </el-table-column>
          <el-table-column label="状态" width="80">
            <template #default="{ row }">
              <el-tag v-if="row.isRestDay === 1" type="danger" size="small">休息</el-tag>
              <el-tag v-else type="success" size="small">上班</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="shiftCode" label="班次" width="80">
            <template #default="{ row }">{{ row.isRestDay === 1 ? '--' : (row.shiftCode || '--') }}</template>
          </el-table-column>
          <el-table-column label="时间" width="150">
            <template #default="{ row }">{{ row.isRestDay === 1 ? '--' : fmtTime(row.startTime) + ' - ' + fmtTime(row.endTime) }}</template>
          </el-table-column>
          <el-table-column label="工时(h)" width="90">
            <template #default="{ row }">{{ row.isRestDay === 1 ? '0' : formatWorkHours(row.workHours) }}</template>
          </el-table-column>
        </el-table>
      </div>
    </el-card>
  </div>
</template>

<script setup>
function formatWorkHours(hours) {
  const num = Number(hours)
  if (!Number.isFinite(num)) return '0.0'
  return num.toFixed(1)
}
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { getMySchedule } from '../api/employee'

const route = useRoute()
const employeeNo = ref(route.query.employeeNo || localStorage.getItem('shift_preview_employee_no') || '')
const loading = ref(false)
const employee = ref(null)
const plans = ref([])
const errorMsg = ref('')
const now = new Date()
const month = ref(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`)

function fmtTime(t) {
  if (!t) return '--'
  const str = String(t)
  if (/^\d{2}:\d{2}/.test(str)) return str.substring(0, 5)
  const match = str.match(/\d{2}:\d{2}/)
  if (match) return match[0]
  try {
    const d = new Date(str)
    if (!Number.isNaN(d.getTime())) {
      return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
    }
  } catch {}
  return '--'
}

async function loadData() {
  loading.value = true
  errorMsg.value = ''
  try {
    const data = await getMySchedule(month.value, employeeNo.value || undefined)
    employee.value = data.employee
    plans.value = data.plans || []
  } catch (e) {
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

onMounted(loadData)
</script>

<style scoped>
.emp-wrap {
  max-width: 1100px;
  margin: 0 auto;
}
.plan-title {
  font-weight: 600;
  margin-bottom: 8px;
  color: #303133;
}
</style>