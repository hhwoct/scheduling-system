<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>审计日志</span>
          <div style="display: flex; gap: 8px; align-items: center">
            <el-select v-model="actionType" placeholder="操作类型" clearable style="width: 180px">
              <el-option label="登录" value="LOGIN" />
              <el-option label="新增员工" value="CREATE_EMPLOYEE" />
              <el-option label="编辑员工" value="UPDATE_EMPLOYEE" />
              <el-option label="停用员工" value="DEACTIVATE_EMPLOYEE" />
              <el-option label="保存技能" value="SAVE_EMPLOYEE_SKILLS" />
              <el-option label="编辑工作站" value="UPDATE_WORKSTATION" />
              <el-option label="编辑班次" value="UPDATE_SHIFT_TEMPLATE" />
              <el-option label="修改规则" value="UPDATE_RULE_CONFIG" />
              <el-option label="生成排班" value="GENERATE_SCHEDULE" />
              <el-option label="调整排班" value="ADJUST_SCHEDULE" />
              <el-option label="发布排班" value="PUBLISH_SCHEDULE" />
              <el-option label="新增工作站" value="CREATE_WORKSTATION" />
              <el-option label="提交请假" value="CREATE_LEAVE" />
              <el-option label="审批请假" value="REVIEW_LEAVE" />
              <el-option label="提交换班" value="CREATE_SWAP" />
              <el-option label="审批换班" value="REVIEW_SWAP" />
            </el-select>
            <el-date-picker
              v-model="dateRange"
              type="daterange"
              value-format="YYYY-MM-DD"
              range-separator="至"
              start-placeholder="开始日期"
              end-placeholder="结束日期"
              style="width: 260px"
            />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: 12px" />

      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column label="操作时间" width="200" show-overflow-tooltip>
          <template #default="{ row }">{{ row.createdAt }}</template>
        </el-table-column>
        <el-table-column prop="operatorName" label="操作人" width="120" />
        <el-table-column label="操作类型" width="180">
          <template #default="{ row }">
            <el-tag size="small">{{ actionName(row.actionType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="targetType" label="操作对象" width="250" />
        <el-table-column prop="remark" label="备注" min-width="220" />
      </el-table>

      <el-pagination
        style="margin-top: 16px"
        layout="total, prev, pager, next"
        :total="total"
        :page-size="pageSize"
        :current-page="page"
        @current-change="handlePageChange"
      />
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import request from '../utils/request'

const loading = ref(false)
const list = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const actionType = ref('')
const dateRange = ref(null)
const errorMsg = ref('')

const actionNames = {
  LOGIN: '登录',
  CREATE_EMPLOYEE: '新增员工',
  UPDATE_EMPLOYEE: '编辑员工',
  DEACTIVATE_EMPLOYEE: '停用员工',
  SAVE_EMPLOYEE_SKILLS: '保存技能',
  UPDATE_WORKSTATION: '编辑工作站',
  UPDATE_SHIFT_TEMPLATE: '编辑班次',
  UPDATE_RULE_CONFIG: '修改规则',
  GENERATE_SCHEDULE: '生成排班',
  ADJUST_SCHEDULE: '调整排班',
  PUBLISH_SCHEDULE: '发布排班',
  CREATE_WORKSTATION: '新增工作站',
  CREATE_LEAVE: '提交请假',
  REVIEW_LEAVE: '审批请假',
  CREATE_SWAP: '提交换班',
  REVIEW_SWAP: '审批换班'
}

function actionName(type) {
  return actionNames[type] || type
}

async function loadData(current = 1) {
  page.value = current
  loading.value = true
  errorMsg.value = ''
  try {
    const params = { page: page.value, pageSize }
    if (actionType.value) params.actionType = actionType.value
    if (dateRange.value && dateRange.value.length === 2) {
      params.startDate = dateRange.value[0]
      params.endDate = dateRange.value[1]
    }
    const res = await request.get('/audit-logs', { params }).then(r => r.data)
    list.value = res.items
    total.value = res.total
  } catch (e) {
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

function handlePageChange(p) {
  loadData(p)
}

onMounted(() => loadData(1))
</script>
