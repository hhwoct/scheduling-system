<template>
  <div>
    <el-card>
      <el-table :data="list" v-loading="loading" border stripe :span-method="spanMethod" @header-dragend="onHeaderDragend">
        <el-table-column label="规则名称" :width="colWidths['规则名称']">
          <template #default="{ row }">
            <!-- 单日最大工时：整行合并，默认收起，展开后列出全部岗位直接编辑 -->
            <div v-if="row.ruleKey === 'max_daily_work_hours'" class="daily-hours-row">
              <div class="daily-hours-header">
                <span class="daily-hours-title">单日最大工时（0h代表不限定）</span>
                <el-button :link="true" type="primary" size="small" @click="dailyHoursExpanded = !dailyHoursExpanded">
                  {{ dailyHoursExpanded ? '收起' : '展开' }}
                </el-button>
                <el-button v-if="dailyHoursExpanded" :link="true" size="small" :disabled="!isSystemAdmin" @click="resetDailyHoursCustom">
                  全部跟随默认
                </el-button>
                <span class="daily-hours-summary">{{ dailyHoursSummary }}</span>
                <span class="daily-hours-switch">
                  <el-switch v-model="dailyHoursRuleStatus" :active-value="1" :inactive-value="0" :disabled="!isSystemAdmin" size="small" />
                  启用
                </span>
              </div>
              <div v-if="dailyHoursExpanded" class="daily-hours-list">
                <div v-for="item in dailyHoursItems" :key="item.key" class="daily-hours-item">
                  <span class="daily-hours-item-label">
                    {{ item.label }}
                    <el-tag v-if="item.custom" size="small" type="warning" style="margin-left:4px">自定义</el-tag>
                  </span>
                  <el-input-number
                    :model-value="item.value"
                    :min="0"
                    :max="168"
                    :precision="1"
                    :controls="false"
                    size="small"
                    :disabled="!isSystemAdmin"
                    style="width:90px"
                    title="小时/天（0 = 不限制）"
                    @update:model-value="v => setDailyHoursValue(item.key, v)"
                  />
                  <span class="daily-hours-unit">小时/天</span>
                </div>
              </div>
            </div>
            <template v-else>{{ row.ruleName }}</template>
          </template>
        </el-table-column>
        <el-table-column prop="ruleKey" label="规则 Key" :width="colWidths['规则 Key']" show-overflow-tooltip />
        <el-table-column label="值" :width="colWidths['值']" header-align="center">
          <template #default="{ row }">
            <!-- 统一 65px 并水平居中 -->
            <div style="display: flex; justify-content: center">
              <!-- 偏好学习权重：0~1 两位小数数字输入，禁止任意字符串 -->
              <el-input-number
                v-if="row.ruleKey === 'preference_learning_weight'"
                :model-value="Number(row.ruleValue)"
                :min="0"
                :max="1"
                :step="0.1"
                :precision="2"
                :controls="false"
                size="small"
                :disabled="!isSystemAdmin"
                class="center-input"
                style="width: 65px"
                title="取值范围 0~1（0 = 只看技能，1 = 完全按店长偏好）"
                @update:model-value="v => (row.ruleValue = v != null ? String(v) : '')"
              />
              <el-input v-else v-model="row.ruleValue" size="small" class="center-input" style="width: 65px" :disabled="!isSystemAdmin" />
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="说明" :width="colWidths['说明']" />
        <el-table-column label="启用" :width="colWidths['启用']">
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
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { getRules, updateRule } from '../api/rules'

const loading = ref(false)
const saving = ref(false)
const list = ref([])

// 列宽：默认值 + localStorage 持久化（拖动表头边界后记住，刷新不重置）
const DEFAULT_COL_WIDTHS = { '规则名称': 260, '规则 Key': 320, '值': 75, '启用': 80 }
const colWidths = ref(loadColWidths())

function loadColWidths() {
  try {
    const saved = JSON.parse(localStorage.getItem('rule_config_col_widths') || '{}')
    return { ...DEFAULT_COL_WIDTHS, ...saved }
  } catch {
    return { ...DEFAULT_COL_WIDTHS }
  }
}

function onHeaderDragend(newWidth, _oldWidth, column) {
  const key = column.label || column.property
  if (!key) return
  colWidths.value[key] = newWidth
  try {
    localStorage.setItem('rule_config_col_widths', JSON.stringify(colWidths.value))
  } catch { /* 存储失败不阻断 */ }
}

const authStore = useAuthStore()
// 仅系统管理员（admin）可修改规则；店长（STORE_MANAGER）只读
const isSystemAdmin = computed(() => authStore.role === 'SYSTEM_ADMIN')

// ============ 单日最大工时（按岗位配置） ============
// 规则值格式：JSON，如 {"default":12,"保洁":10,"楼面":11}；兼容纯数字（全部岗位同一上限）
const dailyHoursDepartments = ['管理', '行政', '工程', '保洁', '楼面', '厨房', '吧台', '兼职']
const dailyHoursMap = reactive({ default: 12 })
const dailyHoursRuleStatus = ref(1)
// 默认收起；展开后列出全部岗位直接编辑
const dailyHoursExpanded = ref(false)
const dailyHoursItems = computed(() => [
  { key: 'default', label: '默认（全局）', value: dailyHoursMap.default, custom: false },
  ...dailyHoursDepartments.map(d => {
    const has = Object.prototype.hasOwnProperty.call(dailyHoursMap, d)
    return { key: d, label: d, value: has ? Number(dailyHoursMap[d]) : Number(dailyHoursMap.default), custom: has }
  })
])
// 收起状态下的摘要：默认值 + 与默认不同的岗位
const dailyHoursSummary = computed(() => {
  const diff = dailyHoursDepartments.filter(d =>
    Object.prototype.hasOwnProperty.call(dailyHoursMap, d) &&
    Number(dailyHoursMap[d]) !== Number(dailyHoursMap.default))
  if (diff.length === 0) return `默认 ${dailyHoursMap.default}h，全部岗位一致`
  return `默认 ${dailyHoursMap.default}h · ` + diff.map(d => `${d} ${dailyHoursMap[d]}h`).join(' · ')
})

// 修改岗位上限：与默认一致 → 回到跟随默认；不同 → 记录为自定义
function setDailyHoursValue(key, v) {
  const val = v == null ? 0 : Number(v)
  if (key === 'default') {
    dailyHoursMap.default = val
    return
  }
  if (val === Number(dailyHoursMap.default)) {
    delete dailyHoursMap[key]
  } else {
    dailyHoursMap[key] = val
  }
}

// 一键清除全部岗位自定义值，全部跟随「默认（全局）」
async function resetDailyHoursCustom() {
  try {
    await ElMessageBox.confirm('清除所有岗位的自定义上限，全部跟随「默认（全局）」？', '提示', { type: 'warning' })
  } catch {
    return
  }
  for (const d of dailyHoursDepartments) {
    delete dailyHoursMap[d]
  }
}

// 单日最大工时整行跨列合并（spanMethod）：第一列合并全部 5 列，其余列隐藏
function spanMethod({ row, columnIndex }) {
  if (row.ruleKey === 'max_daily_work_hours') {
    return columnIndex === 0 ? { rowspan: 1, colspan: 5 } : { rowspan: 0, colspan: 0 }
  }
  return undefined
}

function parseDailyHoursRule(ruleValue) {
  const map = { default: 12 }
  try {
    const parsed = JSON.parse(ruleValue)
    if (parsed !== null && typeof parsed === 'object') {
      for (const k of Object.keys(parsed)) {
        const v = Number(parsed[k])
        map[k] = Number.isNaN(v) ? 0 : v
      }
    } else if (typeof parsed === 'number' && !Number.isNaN(parsed)) {
      map.default = parsed
    }
  } catch {
    const v = Number(ruleValue)
    if (!Number.isNaN(v)) map.default = v
  }
  Object.keys(dailyHoursMap).forEach(k => delete dailyHoursMap[k])
  Object.assign(dailyHoursMap, map)
  // 与默认一致的岗位视为「跟随默认」，不存自定义值
  for (const d of dailyHoursDepartments) {
    if (Number(dailyHoursMap[d]) === Number(dailyHoursMap.default)) {
      delete dailyHoursMap[d]
    }
  }
}

function serializeDailyHoursRule() {
  return JSON.stringify({ ...dailyHoursMap })
}

async function loadData() {
  loading.value = true
  try {
    list.value = await getRules()
    const dailyRule = list.value.find(r => r.ruleKey === 'max_daily_work_hours')
    if (dailyRule) {
      parseDailyHoursRule(dailyRule.ruleValue)
      dailyHoursRuleStatus.value = Number(dailyRule.status)
    }
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
        updateRule(rule.id, {
          ruleValue: rule.ruleKey === 'max_daily_work_hours' ? serializeDailyHoursRule() : rule.ruleValue,
          status: rule.ruleKey === 'max_daily_work_hours' ? dailyHoursRuleStatus.value : rule.status
        })
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

<style scoped>
/* 值列输入框内部文字居中 */
.center-input :deep(input) {
  text-align: center;
}

/* 单日最大工时整行配置（默认收起） */
.daily-hours-row {
  width: 100%;
  padding: 3px 0;
}
.daily-hours-header {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  flex-wrap: wrap;
}
.daily-hours-title {
  font-weight: 600;
  white-space: nowrap;
}
.daily-hours-summary {
  font-size: 12px;
  color: var(--el-text-color-regular);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 120px;
}
.daily-hours-switch {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}
.daily-hours-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px 16px;
  margin-top: 8px;
  padding: 8px 10px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
}
.daily-hours-item {
  display: flex;
  align-items: center;
  gap: 6px;
}
.daily-hours-item-label {
  min-width: 72px;
  display: flex;
  align-items: center;
  font-size: 13px;
  color: var(--el-text-color-primary);
  white-space: nowrap;
}
.daily-hours-unit {
  font-size: 12px;
  color: var(--el-text-color-regular);
  white-space: nowrap;
}
</style>
