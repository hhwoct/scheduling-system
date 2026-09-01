<template>
  <div>
    <el-card>
      <template #header>
        <div class="card-header">
          <span>技能等级总览</span>
          <div class="header-actions">
            <el-input v-model="keyword" placeholder="搜索工号/姓名" clearable size="small" style="width: 180px" />
            <el-select v-model="department" placeholder="全部部门" clearable size="small" style="width: 150px">
              <el-option v-for="d in departments" :key="d" :label="d" :value="d" />
            </el-select>
            <el-checkbox v-model="onlySkilled" size="small" style="margin-left: 4px">只看有技能</el-checkbox>
          </div>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="数字为技能分（0-5，0 表示无该岗位技能）；★ 为主技能岗位。点击任意色块可直接修改（管理员与店长均可操作）。「通岗」= 大部分楼面工作都能做（传送/保洁/咨客/服务等），开启后自动为这些岗位写入至少 3 分，取消时清 0。兼职员工不参与评级。"
        style="margin-bottom: 12px"
      />

      <div v-if="stats" class="stats-line">
        <el-tag size="small">全职员工 {{ stats.totalEmployees }} 人</el-tag>
        <el-tag size="small" type="success">通岗 {{ stats.generalistCount }} 人</el-tag>
        <el-tag size="small" type="success">具备技能岗位 {{ stats.skilledEmployees }} 人</el-tag>
        <el-tag size="small" type="info">工作站 {{ workstations.length }} 个</el-tag>
        <el-tag size="small" type="warning">技能条目 {{ stats.skillCellCount }} 条</el-tag>
      </div>

      <div class="matrix-wrap">
        <el-table :data="filteredEmployees" v-loading="loading" border size="small" :max-height="tableMaxHeight" class="matrix-table">
          <el-table-column label="工号" width="90" fixed align="center">
            <template #default="{ row }">{{ row.employeeNo }}</template>
          </el-table-column>
          <el-table-column label="姓名" width="120" fixed>
            <template #default="{ row }">
              {{ row.name }}
              <el-tag v-if="row.isGeneralist === 1" type="success" size="small" style="margin-left: 4px">通</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="department" label="部门" width="90" fixed />
          <el-table-column label="通岗" width="70" align="center">
            <template #default="{ row }">
              <el-switch
                :model-value="row.isGeneralist === 1"
                :loading="generalistLoading.has(row.id)"
                @change="val => handleGeneralist(row, val)"
              />
            </template>
          </el-table-column>
          <el-table-column label="技能数" width="70" align="center">
            <template #default="{ row }">{{ skilledCount(row) }}</template>
          </el-table-column>
          <el-table-column
            v-for="ws in workstations"
            :key="ws.id"
            :label="ws.name"
            :min-width="72"
            align="center"
          >
            <template #default="{ row }">
              <span
                class="skill-badge editable"
                :class="cellOf(row.id, ws.id)?.skillScore > 0 ? skillClass(cellOf(row.id, ws.id).skillScore) : 'empty'"
                :title="(cellOf(row.id, ws.id)?.skillScore > 0 ? ws.name + '：' + cellOf(row.id, ws.id).skillScore + ' 分' + (cellOf(row.id, ws.id).isPrimarySkill === 1 ? '（主技能）' : '') : ws.name + '：无技能') + '（点击修改）'"
                @click="openEdit(row, ws)"
              >
                {{ cellOf(row.id, ws.id)?.skillScore > 0 ? (cellOf(row.id, ws.id).isPrimarySkill === 1 ? '★' : '') + cellOf(row.id, ws.id).skillScore : '—' }}
              </span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="legend">
        <span class="legend-title">技能等级：</span>
        <span class="sw lv1">1-2 初级</span>
        <span class="sw lv2">3 中级</span>
        <span class="sw lv3">4 熟练</span>
        <span class="sw lv4">5 精通</span>
        <span class="legend-title" style="margin-left: 14px">★ = 主技能岗位（每人仅一个）</span>
      </div>
    </el-card>

    <el-dialog v-model="editVisible" title="修改技能" width="420px">
      <el-form label-width="90px">
        <el-form-item label="员工">
          <span style="font-size: 14px; font-weight: 600">{{ editForm.employeeName }}</span>
        </el-form-item>
        <el-form-item label="岗位">
          <span style="font-size: 14px">{{ editForm.workstationName }}</span>
        </el-form-item>
        <el-form-item label="技能分">
          <el-input-number v-model="editForm.skillScore" :min="0" :max="5" :step="1" style="width: 160px" />
          <span style="margin-left: 8px; font-size: 12px; color: var(--el-text-color-secondary)">0 = 无技能</span>
        </el-form-item>
        <el-form-item label="主技能">
          <el-switch v-model="editForm.isPrimarySkill" :active-value="1" :inactive-value="0" :disabled="editForm.skillScore === 0" />
          <span style="margin-left: 8px; font-size: 12px; color: var(--el-text-color-secondary)">设为该员工的主技能岗位（会取消其他主技能）</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSaveCell">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getSkillMatrix, updateSkillCell, setGeneralist } from '../api/skillMatrix'

const loading = ref(false)
const saving = ref(false)
const workstations = ref([])
const employees = ref([])
const cells = ref([])
const keyword = ref('')
const department = ref('')
const onlySkilled = ref(false)

const editVisible = ref(false)
const editForm = reactive({ employeeId: 0, employeeName: '', workstationId: 0, workstationName: '', skillScore: 0, isPrimarySkill: 0 })

const departments = computed(() => {
  const set = new Set(employees.value.map(e => e.department).filter(Boolean))
  return [...set].sort()
})

// 员工→工作站→技能格 的 Map 索引，避免表格渲染时反复 find/filter
const cellIndex = computed(() => {
  const map = new Map()
  for (const c of cells.value) {
    let byWs = map.get(c.employeeId)
    if (!byWs) {
      byWs = new Map()
      map.set(c.employeeId, byWs)
    }
    byWs.set(c.workstationId, c)
  }
  return map
})

function cellOf(employeeId, workstationId) {
  return cellIndex.value.get(employeeId)?.get(workstationId)
}

function skilledCount(row) {
  const byWs = cellIndex.value.get(row.id)
  if (!byWs) return 0
  let count = 0
  for (const c of byWs.values()) {
    if (c.skillScore > 0) count++
  }
  return count
}

function skillClass(score) {
  if (score <= 2) return 'lv1'
  if (score === 3) return 'lv2'
  if (score === 4) return 'lv3'
  return 'lv4'
}

const filteredEmployees = computed(() => {
  let list = employees.value
  if (department.value) list = list.filter(e => e.department === department.value)
  if (keyword.value) {
    const k = keyword.value.trim().toLowerCase()
    list = list.filter(e => e.employeeNo.toLowerCase().includes(k) || e.name.toLowerCase().includes(k))
  }
  if (onlySkilled.value) list = list.filter(e => skilledCount(e) > 0)
  return list
})

const stats = computed(() => {
  if (!employees.value.length) return null
  const skilledEmployees = employees.value.filter(e => skilledCount(e) > 0).length
  const skillCellCount = cells.value.filter(c => c.skillScore > 0).length
  const generalistCount = employees.value.filter(e => e.isGeneralist === 1).length
  return { totalEmployees: employees.value.length, skilledEmployees, skillCellCount, generalistCount }
})

function openEdit(row, ws) {
  const cell = cellOf(row.id, ws.id)
  editForm.employeeId = row.id
  editForm.employeeName = row.employeeNo + ' ' + row.name
  editForm.workstationId = ws.id
  editForm.workstationName = ws.name
  editForm.skillScore = cell?.skillScore || 0
  editForm.isPrimarySkill = cell?.isPrimarySkill === 1 ? 1 : 0
  editVisible.value = true
}

async function handleSaveCell() {
  saving.value = true
  try {
    const updated = await updateSkillCell({
      employeeId: editForm.employeeId,
      workstationId: editForm.workstationId,
      skillScore: editForm.skillScore,
      isPrimarySkill: editForm.skillScore === 0 ? 0 : editForm.isPrimarySkill
    })
    // 本地更新矩阵：主技能唯一性同步
    cells.value = cells.value
      .filter(c => !(c.employeeId === editForm.employeeId && c.workstationId === editForm.workstationId))
      .map(c => (c.employeeId === editForm.employeeId && updated.isPrimarySkill === 1 ? { ...c, isPrimarySkill: 0 } : c))
    cells.value.push(updated)
    editVisible.value = false
    ElMessage.success('技能已修改（' + editForm.employeeName + ' · ' + editForm.workstationName + ' → ' + updated.skillScore + ' 分）')
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    saving.value = false
  }
}

// 通岗切换（每个员工独立加载态）
const generalistLoading = reactive(new Set())

async function handleGeneralist(row, val) {
  const enable = !!val
  try {
    await ElMessageBox.confirm(
      enable
        ? '将「' + row.name + '」设为通岗？系统会自动为楼面低技能岗位（传送/保洁/咨客/服务等）写入至少 3 分技能。'
        : '取消「' + row.name + '」的通岗？这些楼面低技能岗位的技能分将被清为 0。',
      '通岗设置',
      { confirmButtonText: enable ? '设为通岗' : '取消通岗', cancelButtonText: '再想想', type: 'warning' }
    )
  } catch {
    return
  }
  generalistLoading.add(row.id)
  try {
    const result = await setGeneralist({ employeeId: row.id, isGeneralist: enable ? 1 : 0 })
    row.isGeneralist = result?.isGeneralist
    // 本地更新受影响的格子；响应非全量（缺 cells）时重取矩阵保证一致
    const resultCells = result?.cells ?? []
    if (Array.isArray(result?.cells)) {
      cells.value = cells.value.filter(c => !(c.employeeId === row.id && resultCells.some(x => x.workstationId === c.workstationId)))
      cells.value.push(...resultCells)
    } else {
      await loadData()
    }
    ElMessage.success(enable ? '已设置通岗（楼面岗位 ≥3 分）' : '已取消通岗（楼面岗位清 0）')
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    generalistLoading.delete(row.id)
  }
}

// 表格固定表头：内部滚动，题头始终可见
const tableMaxHeight = ref(560)
function updateTableHeight() {
  tableMaxHeight.value = Math.max(320, window.innerHeight - 330)
}

async function loadData() {
  loading.value = true
  try {
    const data = await getSkillMatrix()
    workstations.value = data.workstations || []
    employees.value = data.employees || []
    cells.value = data.cells || []
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadData()
  updateTableHeight()
  window.addEventListener('resize', updateTableHeight)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', updateTableHeight)
})
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.stats-line {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}
.matrix-wrap {
  overflow-x: auto;
}
.matrix-table {
  min-width: max-content;
}
.skill-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 26px;
  height: 22px;
  padding: 0 5px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.skill-badge.editable {
  cursor: pointer;
}
.skill-badge.editable:hover {
  outline: 2px solid var(--el-color-primary);
  outline-offset: 1px;
}
.skill-badge.empty {
  color: var(--el-text-color-disabled);
  background: var(--el-fill-color-light);
}
.skill-badge.lv1 {
  background: #d9ead3;
}
.skill-badge.lv2 {
  background: #b6d7a8;
}
.skill-badge.lv3 {
  background: #93c47d;
}
.skill-badge.lv4 {
  background: #6aa84f;
  color: var(--el-color-white);
}
.legend {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-top: 12px;
  flex-wrap: wrap;
}
.legend-title {
  font-size: 12px;
  color: var(--el-text-color-regular);
}
.sw {
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: var(--el-text-color-primary);
  border: 1px solid var(--el-border-color);
}
.sw.lv1 {
  background: #d9ead3;
}
.sw.lv2 {
  background: #b6d7a8;
}
.sw.lv3 {
  background: #93c47d;
}
.sw.lv4 {
  background: #6aa84f;
  color: var(--el-color-white);
}
</style>
