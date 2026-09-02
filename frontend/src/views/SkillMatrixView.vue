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
            <el-checkbox class="u-ml-2" v-model="onlySkilled" size="small">只看有技能</el-checkbox>
          </div>
        </div>
      </template>

      <el-alert class="u-mb-5"
        type="info"
        :closable="false"
        show-icon
        title="数字为技能分（0-5，0 表示无该岗位技能）；★ 为主技能岗位。点击任意色块可直接修改（管理员与店长均可操作）。「通岗」= 大部分楼面工作都能做（传送/保洁/咨客/服务等），开启后自动为这些岗位写入至少 3 分，取消时清 0。兼职员工不参与评级。"
       
      />

      <div v-if="stats" class="stats-line">
        <el-tag size="small">全职员工 {{ stats.totalEmployees }} 人</el-tag>
        <el-tag size="small" type="success">通岗 {{ stats.generalistCount }} 人</el-tag>
        <el-tag size="small" type="success">具备技能岗位 {{ stats.skilledEmployees }} 人</el-tag>
        <el-tag size="small" type="info">工作站 {{ workstations.length }} 个</el-tag>
        <el-tag size="small" type="warning">技能条目 {{ stats.skillCellCount }} 条</el-tag>
      </div>

      <div class="matrix-wrap">
        <el-table :data="filteredEmployees" v-loading="loading" border size="small" class="matrix-table" :cell-style="{ textAlign: 'center' }" :header-cell-style="{ textAlign: 'center', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }">
          <el-table-column label="工号" min-width="35" show-overflow-tooltip fixed align="center">
            <template #default="{ row }">{{ row.employeeNo }}</template>
          </el-table-column>
          <el-table-column label="姓名" min-width="75" show-overflow-tooltip fixed>
            <template #default="{ row }">{{ row.name }}</template>
          </el-table-column>
          <el-table-column prop="department" label="部门" min-width="45" show-overflow-tooltip fixed />
          <el-table-column label="通岗" min-width="50" show-overflow-tooltip align="center">
            <template #default="{ row }">
              <el-switch
                :model-value="row.isGeneralist === 1"
                :loading="generalistLoading.has(row.id)"
                @change="val => handleGeneralist(row, val)"
              />
            </template>
          </el-table-column>
          <el-table-column label="技能数" min-width="60" show-overflow-tooltip align="center">
            <template #default="{ row }">{{ skilledCount(row) }}</template>
          </el-table-column>
          <el-table-column
            v-for="ws in workstations"
            :key="ws.id"
            :label="ws.name"
            :min-width="wsColumnWidth(ws)" show-overflow-tooltip
            align="center"
          >
            <template #default="{ row }">
              <span
                class="skill-score"
                :title="(cellOf(row.id, ws.id)?.skillScore > 0 ? ws.name + '：' + cellOf(row.id, ws.id).skillScore + ' 分' + (cellOf(row.id, ws.id).isPrimarySkill === 1 ? '（主技能）' : '') : ws.name + '：无技能') + '（点击修改）'"
                @click="openEdit(row, ws)"
              >{{ cellOf(row.id, ws.id)?.skillScore > 0 ? (cellOf(row.id, ws.id).isPrimarySkill === 1 ? '★' : '') + cellOf(row.id, ws.id).skillScore : '—' }}</span>
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
        <span class="legend-title u-ml-6">★ = 主技能岗位（每人仅一个）</span>
      </div>
    </el-card>

    <el-dialog v-model="editVisible" title="修改技能" width="420px">
      <el-form label-width="90px">
        <el-form-item label="员工">
          <span style="font-size: var(--app-font-md); font-weight: 600">{{ editForm.employeeName }}</span>
        </el-form-item>
        <el-form-item label="岗位">
          <span style="font-size: var(--app-font-md)">{{ editForm.workstationName }}</span>
        </el-form-item>
        <el-form-item label="技能分">
          <el-input-number v-model="editForm.skillScore" :min="0" :max="5" :step="1" style="width: 160px" />
          <span class="u-text-hint u-ml-4">0 = 无技能</span>
        </el-form-item>
        <el-form-item label="主技能">
          <el-switch v-model="editForm.isPrimarySkill" :active-value="1" :inactive-value="0" :disabled="editForm.skillScore === 0" />
          <span class="u-text-hint u-ml-4">设为该员工的主技能岗位（会取消其他主技能）</span>
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

// 工作站列宽:四字名(文员仓管/工程维修/网络维护/客户经理)放宽,两字名紧凑
function wsColumnWidth(ws) {
  const name = ws?.name || ''
  const len = [...name].length
  return len >= 4 ? 66 : 44
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

// 表格不再限高,整页滚动(与全站口径一致)
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

})

onBeforeUnmount(() => {

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
  gap: var(--app-space-4);
}
.stats-line {
  display: flex;
  gap: var(--app-space-4);
  flex-wrap: wrap;
  margin-bottom: var(--app-space-5);
}
.matrix-wrap {
  /* 矩阵填满容器,等比例拉伸;无内部滚动 */
  overflow-x: hidden;
}
.matrix-table {
  width: 100%;
}
/* 表头强制单行:不换行,超长省略号 */
.matrix-table :deep(.el-table__header .cell) {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.skill-score {
  cursor: pointer;
  font-size: var(--app-font-base);
}
.legend {
  display: flex;
  align-items: center;
  gap: var(--app-space-3);
  padding-top: var(--app-space-5);
  flex-wrap: wrap;
}
.legend-title {
  font-size: var(--app-font-sm);
  color: var(--el-text-color-regular);
}
.sw {
  padding: var(--app-space-1) var(--app-space-4);
  border-radius: var(--app-radius-sm);
  font-size: var(--app-font-xs);
  color: var(--el-text-color-primary);
  border: 1px solid var(--el-border-color);
}
.sw.lv1 {
  background: var(--app-level-1);
}
.sw.lv2 {
  background: var(--app-level-2);
}
.sw.lv3 {
  background: var(--app-level-3);
}
.sw.lv4 {
  background: var(--app-level-4);
  color: var(--el-color-white);
}
</style>
