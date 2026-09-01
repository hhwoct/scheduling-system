<template>
  <div class="emp-wrap">
    <el-card>
      <template #header>换班申请</template>

      <el-form :model="form" label-width="80px" style="max-width: 500px">
        <el-form-item label="排班计划" required>
          <el-select v-model="form.planId" style="width: 100%" @change="onPlanChange">
            <el-option v-for="p in plans" :key="p.id" :label="`${p.planName}（${p.startDate} ~ ${p.endDate}）`" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="换班日期" required>
          <el-date-picker
            v-model="form.swapDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
            :disabled-date="disabledDate"
            @change="onDateChange"
          />
        </el-form-item>
        <el-form-item label="换班同事" required>
          <el-select v-model="form.targetEmployeeId" style="width: 100%" :disabled="!form.swapDate || !form.planId">
            <el-option v-for="c in candidates" :key="c.id" :label="`${c.name}（${c.department}）`" :value="c.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="换班原因">
          <el-input v-model="form.reason" type="textarea" :rows="2" placeholder="请输入换班原因（选填）" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="submitting" @click="handleSubmit">提交申请</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card style="margin-top: var(--app-space-6)">
      <template #header>我的换班记录</template>
      <el-table :data="mine" v-loading="loading" border stripe size="small">
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column label="换班日期" width="120">
          <template #default="{ row }">{{ row.swapDate }}</template>
        </el-table-column>
        <el-table-column label="换班对象" width="200">
          <template #default="{ row }">
            我 ↔ {{ row.target?.name || '--' }}（{{ row.target?.department || '--' }}）
          </template>
        </el-table-column>
        <el-table-column prop="reason" label="原因" min-width="150" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small">{{ statusName(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="审批意见" min-width="120">
          <template #default="{ row }">{{ row.reviewRemark || '--' }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getMySwaps, submitSwap, getSwapCandidates } from '../api/swap'
import { getSwapPlans } from '../api/schedules'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const authStore = useAuthStore()
// 普通员工身份一律取当前登录身份；管理员预览仅非员工角色可通过 query/localStorage 指定目标
const employeeNo = computed(() => {
  if (authStore.role === 'EMPLOYEE') return authStore.user?.employeeNo || ''
  return route.query.employeeNo || localStorage.getItem('shift_preview_employee_no') || ''
})
const form = reactive({ planId: null, swapDate: '', targetEmployeeId: null, reason: '' })
const mine = ref([])
const plans = ref([])
const candidates = ref([])
const loading = ref(false)
const submitting = ref(false)

function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已批准', REJECTED: '已驳回' }[s] || s
}
function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

function disabledDate(d) {
  // 每次计算当天零点，避免跨午夜后日期选择器用旧值
  const today0 = new Date()
  today0.setHours(0, 0, 0, 0)
  if (d < today0) return true

  const plan = plans.value.find(p => p.id === form.planId)
  if (plan) {
    const start = new Date(plan.startDate + 'T00:00:00')
    const end = new Date(plan.endDate + 'T00:00:00')
    if (d < start || d > end) return true
  }

  return false
}

// 加载已发布排班计划（使用员工可访问的端点）
async function loadPlans() {
  try {
    const allPlans = await getSwapPlans()
    plans.value = allPlans || []
  } catch (e) {
    ElMessage.error('加载排班计划失败：' + (e.message || '网络错误'))
  }
}

// 请求序号：仅应用最新一次响应，避免并发乱序覆盖
let candidatesSeq = 0

async function loadCandidates() {
  if (!form.planId || !form.swapDate) {
    candidates.value = []
    form.targetEmployeeId = null
    return
  }
  const seq = ++candidatesSeq
  try {
    const data = await getSwapCandidates(form.planId, form.swapDate)
    if (seq !== candidatesSeq) return
    candidates.value = data || []
    form.targetEmployeeId = null
  } catch (e) {
    if (seq !== candidatesSeq) return
    console.error('加载换班同事失败', e)
    ElMessage.error('加载换班同事失败：' + (e?.message || e?.toString?.() || '未知错误'))
    candidates.value = []
  }
}

async function loadMine() {
  loading.value = true
  try {
    mine.value = await getMySwaps(employeeNo.value || undefined)
  } finally {
    loading.value = false
  }
}

function onPlanChange() {
  form.swapDate = ''
  form.targetEmployeeId = null
  candidates.value = []
}

function onDateChange() {
  loadCandidates()
}

async function handleSubmit() {
  if (!form.planId) {
    ElMessage.warning('请选择排班计划')
    return
  }
  if (!form.swapDate) {
    ElMessage.warning('请选择换班日期')
    return
  }
  if (!form.targetEmployeeId) {
    ElMessage.warning('请选择换班同事')
    return
  }
  submitting.value = true
  try {
    await submitSwap({ ...form, reason: form.reason || undefined })
    ElMessage.success('换班申请已提交')
    form.reason = ''
    form.targetEmployeeId = null
    loadMine()
  } finally {
    submitting.value = false
  }
}

onMounted(() => {
  loadPlans()
  loadMine()
})
</script>

<style scoped>
.emp-wrap { max-width: 1100px; margin: 0 auto; }
</style>