<template>
  <div class="emp-wrap">
    <el-card>
      <template #header>请假申请</template>

      <el-form :model="form" label-width="80px" style="max-width: 460px">
        <el-form-item label="请假类型" required>
          <el-select v-model="form.leaveType" style="width: 100%">
            <el-option label="事假" value="PERSONAL" />
            <el-option label="病假" value="SICK" />
            <el-option label="年假" value="ANNUAL" />
            <el-option label="其他" value="OTHER" />
          </el-select>
        </el-form-item>
        <el-form-item label="开始日期" required>
          <el-date-picker
            v-model="form.startDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
            :disabled-date="disabledStartDate"
          />
        </el-form-item>
        <el-form-item label="结束日期" required>
          <el-date-picker
            v-model="form.endDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
            :disabled-date="disabledEndDate"
          />
        </el-form-item>
        <el-form-item label="请假原因">
          <el-input v-model="form.reason" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="submitting" @click="handleSubmit">提交申请</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card style="margin-top: 16px">
      <template #header>我的请假记录</template>
      <el-table :data="mine" v-loading="loading" border stripe size="small">
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column label="类型" width="90">
          <template #default="{ row }">{{ typeName(row.leaveType) }}</template>
        </el-table-column>
        <el-table-column label="期间" width="210">
          <template #default="{ row }">{{ row.startDate }} ~ {{ row.endDate }}</template>
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
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getMyLeaves, submitLeave } from '../api/leave'

const form = reactive({ leaveType: 'PERSONAL', startDate: '', endDate: '', reason: '' })
const mine = ref([])
const loading = ref(false)
const submitting = ref(false)

// 请假日期范围：不能早于今天，不能晚于 30 天后
const TODAY0 = new Date()
TODAY0.setHours(0, 0, 0, 0)
const MAX_DATE = new Date(TODAY0)
MAX_DATE.setDate(TODAY0.getDate() + 30)

function formatDate(d) {
  return d ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` : ''
}

function toDate(str) {
  return str ? new Date(str + 'T00:00:00') : null
}

function disabledStartDate(d) {
  if (d < TODAY0 || d > MAX_DATE) return true
  if (form.endDate && d > toDate(form.endDate)) return true
  return false
}

function disabledEndDate(d) {
  if (d < TODAY0 || d > MAX_DATE) return true
  if (form.startDate && d < toDate(form.startDate)) return true
  return false
}

function typeName(t) {
  return { PERSONAL: '事假', SICK: '病假', ANNUAL: '年假', OTHER: '其他' }[t] || t
}
function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已批准', REJECTED: '已驳回' }[s] || s
}
function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

async function loadMine() {
  loading.value = true
  try {
    mine.value = await getMyLeaves()
  } finally {
    loading.value = false
  }
}

async function handleSubmit() {
  if (!form.startDate || !form.endDate) {
    ElMessage.warning('请选择请假起止日期')
    return
  }
  if (form.startDate > form.endDate) {
    ElMessage.warning('开始日期不能晚于结束日期')
    return
  }
  if (form.startDate < formatDate(TODAY0)) {
    ElMessage.warning('请假开始日期不能早于今天')
    return
  }
  if (form.startDate > formatDate(MAX_DATE)) {
    ElMessage.warning('请假日期不能晚于 30 天后')
    return
  }
  submitting.value = true
  try {
    await submitLeave({ ...form, reason: form.reason || undefined })
    ElMessage.success('请假申请已提交')
    form.reason = ''
    loadMine()
  } finally {
    submitting.value = false
  }
}

onMounted(loadMine)
</script>

<style scoped>
.emp-wrap { max-width: 1100px; margin: 0 auto; }
</style>