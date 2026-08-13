<template>
  <div>
    <el-card>
      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="ruleName" label="规则名称" width="220" />
        <el-table-column prop="ruleKey" label="规则 Key" width="200" />
        <el-table-column label="值" width="160">
          <template #default="{ row }">
            <el-input v-model="row.ruleValue" size="small" style="width: 120px" />
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="说明" />
        <el-table-column label="启用" width="80">
          <template #default="{ row }">
            <el-switch v-model="row.status" :active-value="1" :inactive-value="0" />
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top: 16px; text-align: right">
        <el-button type="primary" :loading="saving" @click="handleSave">保存全部</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getRules, updateRule } from '../api/rules'

const loading = ref(false)
const saving = ref(false)
const list = ref([])

async function loadData() {
  loading.value = true
  try {
    list.value = await getRules()
  } finally {
    loading.value = false
  }
}

// P3-35: 并发保存所有规则，失败不中断其他规则
async function handleSave() {
  if (list.value.length === 0) {
    ElMessage.info('没有可保存的规则')
    return
  }
  saving.value = true
  try {
    const results = await Promise.allSettled(
      list.value.map(rule =>
        updateRule(rule.id, { ruleValue: rule.ruleValue, status: rule.status, version: rule.version })
      )
    )
    const failed = results.filter(r => r.status === 'rejected')
    if (failed.length === 0) {
      ElMessage.success(`保存成功（${list.value.length}条）`)
    } else {
      ElMessage.warning(`${failed.length}条保存失败，请重试`)
    }
  } finally {
    saving.value = false
  }
}

onMounted(loadData)
</script>
