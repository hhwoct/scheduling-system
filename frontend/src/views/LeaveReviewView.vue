<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>请假审批</span>
          <el-radio-group v-model="statusFilter" @change="loadData">
            <el-radio-button label="">全部</el-radio-button>
            <el-radio-button label="PENDING">待审批</el-radio-button>
            <el-radio-button label="APPROVED">已批准</el-radio-button>
            <el-radio-button label="REJECTED">已驳回</el-radio-button>
          </el-radio-group>
        </div>
      </template>

      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column label="员工" width="160">
          <template #default="{ row }">{{ row.employee?.name || '--' }}（{{ row.employee?.employeeNo || '--' }}）</template>
        </el-table-column>
        <el-table-column prop="employee" label="部门" width="100">
          <template #default="{ row }">{{ row.employee?.department || '--' }}</template>
        </el-table-column>
        <el-table-column label="类型" width="90">
          <template #default="{ row }">{{ typeName(row.leaveType) }}</template>
        </el-table-column>
        <el-table-column label="期间" width="400">
          <template #default="{ row }">{{ row.startDate }} ~ {{ row.endDate }}</template>
        </el-table-column>
        <el-table-column prop="reason" label="原因" min-width="140" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small">{{ statusName(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="审批意见" min-width="110">
          <template #default="{ row }">{{ row.reviewRemark || '--' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="150">
          <template #default="{ row }">
            <template v-if="row.status === 'PENDING'">
              <el-button link type="success" @click="handleReview(row, true)">批准</el-button>
              <el-button link type="danger" @click="handleReview(row, false)">驳回</el-button>
            </template>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { getLeaveReviewList, reviewLeave } from '../api/leave'

const list = ref([])
const loading = ref(false)
const statusFilter = ref('')

function typeName(t) {
  return { PERSONAL: '事假', SICK: '病假', ANNUAL: '年假', OTHER: '其他' }[t] || t
}
function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已批准', REJECTED: '已驳回' }[s] || s
}
function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

async function loadData() {
  loading.value = true
  try {
    list.value = await getLeaveReviewList(statusFilter.value || undefined)
  } finally {
    loading.value = false
  }
}

async function handleReview(row, approved) {
  const action = approved ? '批准' : '驳回'
  const { value: remark } = await ElMessageBox.prompt(`请输入${action}意见（可留空）`, `${action}请假申请 #${row.id}`, {
    confirmButtonText: '确定',
    cancelButtonText: '取消',
    inputPlaceholder: '审批意见...'
  }).catch(() => ({ value: '' }))
  await reviewLeave(row.id, { approved, remark: remark || undefined })
  loadData()
}

onMounted(loadData)
</script>