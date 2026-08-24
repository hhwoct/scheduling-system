<template>
  <div>
    <el-card>
      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="ruleName" label="规则名称" width="260" show-overflow-tooltip />
        <el-table-column prop="ruleKey" label="规则 Key" width="320" show-overflow-tooltip />
        <el-table-column label="值" width="160">
          <template #default="{ row }">
            <el-input v-model="row.ruleValue" size="small" style="width: 120px" :disabled="!isSystemAdmin" />
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="说明" />
        <el-table-column label="启用" width="80">
          <template #default="{ row }">
            <el-switch v-model="row.status" :active-value="1" :inactive-value="0" :disabled="!isSystemAdmin" />
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top: 16px; text-align: right">
        <el-button v-if="isSystemAdmin" type="primary" :loading="saving" @click="handleSave">保存全部</el-button>
        <el-alert v-else type="info" :closable="false" show-icon title="仅系统管理员可修改排班规则，当前为只读模式" />
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { getRules, updateRule } from '../api/rules'

const loading = ref(false)
const saving = ref(false)
const list = ref([])

const authStore = useAuthStore()
// 仅系统管理员（admin）可修改规则；店长（STORE_MANAGER）只读
const isSystemAdmin = computed(() => authStore.role === 'SYSTEM_ADMIN')

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
        updateRule(rule.id, { ruleValue: rule.ruleValue, status: rule.status })
      )
    )
    const failed = []
    results.forEach((r, i) => {
      if (r.status === 'rejected') failed.push(list.value[i].ruleName || list.value[i].ruleKey || ('#' + list.value[i].id))
    })
    // 保存后重载，回写服务端最新值并对齐本地状态（后端 RuleConfigItem 无 version 字段）
    try {
      await loadData()
    } catch { /* 重载失败不阻断提示 */ }
    if (failed.length === 0) {
      ElMessage.success(`保存成功（${list.value.length}条）`)
    } else {
      ElMessage.warning(`${failed.length}条保存失败：${failed.join('、')}`)
    }
  } finally {
    saving.value = false
  }
}

onMounted(loadData)
</script>
