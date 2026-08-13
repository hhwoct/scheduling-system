<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>换班审批</span>
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
        <el-table-column label="申请人" width="160">
          <template #default="{ row }">{{ row.requester?.name || '--' }}（{{ row.requester?.employeeNo || '--' }}）</template>
        </el-table-column>
        <el-table-column label="申请人部门" width="100">
          <template #default="{ row }">{{ row.requester?.department || '--' }}</template>
        </el-table-column>
        <el-table-column label="换班对象" width="160">
          <template #default="{ row }">{{ row.target?.name || '--' }}（{{ row.target?.employeeNo || '--' }}）</template>
        </el-table-column>
        <el-table-column label="对象部门" width="100">
          <template #default="{ row }">{{ row.target?.department || '--' }}</template>
        </el-table-column>
        <el-table-column prop="swapDate" label="换班日期" width="120" />
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
import { ElMessage, ElMessageBox } from 'element-plus'
import { getSwapReviewList, reviewSwap } from '../api/swap'

const list = ref([])
const loading = ref(false)
const statusFilter = ref('')

function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已批准', REJECTED: '已驳回' }[s] || s
}
function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

async function loadData() {
  loading.value = true
  try {
    list.value = await getSwapReviewList(statusFilter.value || undefined)
  } finally {
    loading.value = false
  }
}

async function handleReview(row, approved) {
  if (row._submitting) return
  row._submitting = true

  const action = approved ? '批准' : '驳回'
  try {
    const result = await ElMessageBox.prompt(`请输入${action}意见（可留空）`, `${action}换班申请 #${row.id}`, {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      inputPlaceholder: '审批意见...'
    })
    // 只有确认才执行
    await reviewSwap(row.id, { approved, remark: result.value || undefined })
    ElMessage.success(`${action}成功`)
    await loadData()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error('操作失败，请重试')
  } finally {
    row._submitting = false
  }
}

onMounted(loadData)
</script>