<template>
  <div>
    <el-card>
      <template #header>
        <div class="u-row-between">
          <span>审计日志</span>
          <div class="u-row u-gap-4">
            <el-select v-model="actionType" placeholder="操作类型" clearable style="width: 180px">
              <el-option v-for="opt in actionOptions" :key="opt.value" :label="opt.label" :value="opt.value" />
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
            <el-button type="primary" :loading="loading" @click="loadData(1)">查询</el-button>
          </div>
        </div>
      </template>

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

      <el-pagination class="u-mt-6"
       
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

// P3-25: 请求序号防止旧响应覆盖
let requestSeq = 0

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

// 单一数据源派生筛选选项，供下拉框 v-for
const actionOptions = Object.entries(actionNames).map(([value, label]) => ({ value, label }))

async function loadData(current = 1) {
  const seq = ++requestSeq
  page.value = current
  loading.value = true
  try {
    const params = { page: page.value, pageSize }
    if (actionType.value) params.actionType = actionType.value
    if (dateRange.value && dateRange.value.length === 2) {
      params.startDate = dateRange.value[0]
      params.endDate = dateRange.value[1]
    }
    const res = await request.get('/audit-logs', { params }).then(r => r.data)
    // 只应用最新请求的结果
    if (seq === requestSeq) {
      list.value = res.items
      total.value = res.total
    }
  } catch (e) {
    // 错误提示由响应拦截器统一处理
  } finally {
    if (seq === requestSeq) {
      loading.value = false
    }
  }
}

function handlePageChange(p) {
  loadData(p)
}

onMounted(() => loadData(1))
</script>
