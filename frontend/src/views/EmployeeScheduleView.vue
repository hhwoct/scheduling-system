<template>
  <div class="emp-wrap">
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>我的班表</span>
          <div>
            <!-- 管理员/店长预览：选择员工（放时间选择器旁边） -->
            <el-select
              v-if="authStore.role && authStore.role !== 'EMPLOYEE'"
              :model-value="employeeNo || undefined"
              placeholder="选择预览员工"
              clearable
              filterable
              style="width: 160px; margin-right: var(--app-space-4)"
              @update:model-value="onPreviewChange"
            >
              <el-option
                v-for="emp in employeeList"
                :key="emp.employeeNo"
                :label="`${emp.employeeNo} ${emp.name}`"
                :value="emp.employeeNo"
              />
            </el-select>
            <el-date-picker
              v-model="month"
              type="month"
              value-format="YYYY-MM"
              :clearable="false"
              style="width: 140px; margin-right: var(--app-space-4)"
            />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <!-- 员工信息 -->
      <el-descriptions v-if="employee" :column="4" border style="margin-bottom: var(--app-space-5)">
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
          <el-table-column label="休息" min-width="170">
            <template #default="{ row }">
              <span v-if="row.isRestDay === 1 || !row.breakStartTime">--</span>
              <span v-else>
                <el-tag size="small" type="warning">休 {{ fmtTime(row.breakStartTime) }}-{{ fmtTime(row.breakEndTime) }}</el-tag>
                <span v-if="row.coverEmployeeName" class="cover-note">由 {{ row.coverEmployeeName }} 顶班</span>
              </span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 顶岗记录：独立于具体计划，全局展示 -->
      <div v-if="covers.length" class="cover-list">
        <div class="cover-title">我顶岗的记录</div>
        <el-table :data="covers" border stripe size="small" max-height="200" style="margin-top: var(--app-space-3)">
          <el-table-column prop="workDate" label="日期" width="120" />
          <el-table-column label="时段" width="130">
            <template #default="{ row }">{{ fmtTime(row.breakStartTime) }}-{{ fmtTime(row.breakEndTime) }}</template>
          </el-table-column>
          <el-table-column prop="workstationName" label="顶岗岗位" width="120">
            <template #default="{ row }">{{ row.workstationName || '--' }}</template>
          </el-table-column>
          <el-table-column prop="forEmployeeName" label="替谁顶岗" />
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
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { getMySchedule } from '../api/employee'
import { getEmployees } from '../api/employees'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const employeeNo = ref(Array.isArray(route.query.employeeNo) ? route.query.employeeNo[0] : (route.query.employeeNo || localStorage.getItem('shift_preview_employee_no') || ''))
const loading = ref(false)
const employee = ref(null)
const plans = ref([])
const covers = ref([])
const errorMsg = ref('')
const employeeList = ref([])
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

// 管理员/店长加载员工列表（分页拉全量）供预览选择
async function loadEmployees() {
  const role = authStore.role || localStorage.getItem('shift_role') || ''
  if (role === 'EMPLOYEE') return
  try {
    const pageSize = 100
    let page = 1
    let all = []
    let total = 0
    do {
      const res = await getEmployees({ page, pageSize, status: 1 })
      const items = res.items || []
      all = all.concat(items)
      total = res.total || 0
      page++
      if (items.length === 0) break
    } while (all.length < total)
    // 兼职员工人员不固定、无固定班表，预览查询无意义，只保留全职
    employeeList.value = all.filter(e => !e.isParttime)
    // 兜底：无有效预览员工（未选/选了兼职/档案不存在）时自动选第一个全职员工，
    // 否则后端按当前登录账号查档案（admin 等账号无档案）会报「员工档案不存在」
    const exists = employeeNo.value && employeeList.value.some(e => e.employeeNo === employeeNo.value)
    if (!exists && employeeList.value.length > 0) {
      const first = employeeList.value[0]
      employeeNo.value = first.employeeNo
      localStorage.setItem('shift_preview_employee_no', first.employeeNo)
      router.replace({ path: route.path, query: { employeeNo: first.employeeNo } })
    }
  } catch (e) {
    console.error('加载员工列表失败', e)
  }
}

// 切换预览员工：保存到 localStorage 并更新路由（watch 路由自动重载）
function onPreviewChange(val) {
  const v = val || ''
  if (v) localStorage.setItem('shift_preview_employee_no', v)
  else localStorage.removeItem('shift_preview_employee_no')
  router.push({ path: route.path, query: v ? { employeeNo: v } : {} })
}

async function loadData() {
  loading.value = true
  errorMsg.value = ''
  try {
    const data = await getMySchedule(month.value, employeeNo.value || undefined)
    employee.value = data?.employee || null
    plans.value = data?.plans || []
    covers.value = data?.covers || []
  } catch (e) {
    employee.value = null
    plans.value = []
    covers.value = []
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

// 预览员工（query employeeNo）变化时重载
watch(() => route.query.employeeNo, (val) => {
  employeeNo.value = Array.isArray(val) ? val[0] : (val || localStorage.getItem('shift_preview_employee_no') || '')
  loadData()
})

// 月份变化自动重载；清空时回退当前月
watch(month, () => {
  if (!month.value) {
    const now = new Date()
    month.value = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  }
  loadData()
})

onMounted(() => {
  loadEmployees()
  loadData()
})
</script>

<style scoped>
.emp-wrap {
  max-width: 1100px;
  margin: 0 auto;
}
.plan-title {
  font-weight: 600;
  margin-bottom: var(--app-space-4);
  color: var(--el-text-color-primary);
}
.cover-note {
  margin-left: var(--app-space-3);
  font-size: var(--app-font-sm);
  color: var(--el-color-primary);
}
.cover-list {
  margin-top: 10px;
}
.cover-title {
  font-weight: 600;
  color: var(--el-color-warning);
  font-size: var(--app-font-base);
}
</style>