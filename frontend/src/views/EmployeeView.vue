<template>
  <div class="page">
    <el-card>
      <el-form inline :model="query">
        <el-form-item label="姓名">
          <el-input v-model="query.name" placeholder="姓名" clearable style="width: 160px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="工号">
          <el-input v-model="query.employeeNo" placeholder="工号" clearable style="width: 160px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="部门">
          <el-input v-model="query.department" placeholder="部门(模糊)" clearable style="width: 140px" @keyup.enter="search" @clear="search" />
        </el-form-item>
        <el-form-item v-if="isSystemAdmin" label="门店">
          <el-select v-model="query.storeId" placeholder="全部门店" clearable style="width: 160px" @change="search">
            <el-option v-for="s in stores" :key="s.id" :label="s.name" :value="s.id" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
        </el-form-item>
      </el-form>

      <el-button class="u-mb-5" type="primary" @click="openCreate">新增员工</el-button>

      <el-table :data="list" v-loading="loading" border stripe size="small" style="width: 1040px; max-width: 100%" :cell-style="{ textAlign: 'center' }" :header-cell-style="{ textAlign: 'center' }">
        <el-table-column prop="employeeNo" label="工号" min-width="76" show-overflow-tooltip />
        <el-table-column label="姓名" min-width="108" show-overflow-tooltip>
          <template #default="{ row }">{{ row.name }}<el-tag class="u-ml-1" v-if="row.isParttime === 1" type="warning" size="small">兼</el-tag></template>
        </el-table-column>
        <el-table-column v-if="isSystemAdmin" prop="storeName" label="门店" min-width="110" show-overflow-tooltip>
          <template #default="{ row }">{{ row.storeName || '--' }}</template>
        </el-table-column>
        <el-table-column prop="department" label="部门" min-width="66" show-overflow-tooltip />
        <el-table-column prop="primaryPosition" label="主岗" min-width="88" show-overflow-tooltip />
        <el-table-column prop="phone" label="手机号" min-width="112" show-overflow-tooltip />
        <el-table-column label="周工时" min-width="92" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.maxWeeklyHours }}h
            <el-tag class="u-ml-1" v-if="row.weeklyHoursFollowDefault === 1" size="small" type="info">默认</el-tag>
            <el-tag class="u-ml-1" v-else size="small" type="warning">自定</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" min-width="90" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'">{{ row.status === 1 ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="是否休假" min-width="110" show-overflow-tooltip>
          <template #default="{ row }">
            <template v-if="leavePeriods(row).length">
              <el-tag class="u-mb-1" v-if="onLeaveNow(row)" type="danger" size="small">休假中</el-tag>
              <div v-for="(p, i) in leavePeriods(row)" :key="i" class="leave-line" :class="{ 'leave-early': p.earlyReturned }" :title="(p.earlyReturned ? '提前返岗 · ' : '') + p.full">
                {{ p.shortNoSpace }}
              </div>
            </template>
          </template>
        </el-table-column>
        <el-table-column label="操作" min-width="170" fixed="right">
          <template #default="{ row }">
            <el-button :link="true" type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button :link="true" type="primary" @click="openSkills(row)">技能</el-button>
            <el-button v-if="row.status === 1" :link="true" type="danger" :disabled="deactivating" @click="handleDeactivate(row)">停用</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination class="u-mt-6"
       
        layout="total, prev, pager, next"
        :total="total"
        :page-size="query.pageSize"
        :current-page="query.page"
        @current-change="handlePageChange"
      />
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑员工' : '新增员工'" width="500px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px" class="dialog-form">
        <el-form-item label="工号" prop="employeeNo">
          <el-input v-model="form.employeeNo" />
        </el-form-item>
        <el-form-item label="姓名" prop="name">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item v-if="isSystemAdmin" label="门店" prop="storeId">
          <el-select
            class="u-w-full"
            v-model="form.storeId"
            placeholder="请选择门店"
            :disabled="editing"
            :title="editing ? '员工所属门店创建后不可修改' : ''"
          >
            <el-option v-for="s in stores" :key="s.id" :label="s.name" :value="s.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="部门" prop="department">
          <el-select class="u-w-full" v-model="form.department">
            <el-option v-for="d in departments" :key="d" :label="d" :value="d" />
          </el-select>
        </el-form-item>
        <el-form-item label="主岗" prop="primaryPosition">
          <el-input v-model="form.primaryPosition" />
        </el-form-item>
        <el-form-item label="手机号">
          <el-input v-model="form.phone" />
        </el-form-item>
        <el-form-item label="跟随默认">
          <el-switch v-model="form.weeklyHoursFollowDefault" :active-value="1" :inactive-value="0" />
          <span class="follow-hint">跟随全局「最大周工时」{{ globalMaxWeeklyHours }}h</span>
        </el-form-item>
        <el-form-item label="周工时上限" prop="maxWeeklyHours">
          <el-input-number v-model="form.maxWeeklyHours" :min="1" :max="168" :disabled="form.weeklyHoursFollowDefault === 1" />
          <span v-if="form.weeklyHoursFollowDefault === 1" class="follow-hint">保存后自动取 {{ globalMaxWeeklyHours }}h</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" :disabled="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="skillsVisible" :title="'技能配置 - ' + (currentEmployee?.name || '')" width="600px">
      <el-table :data="skillRows" v-loading="skillsLoading" border stripe size="small">
        <el-table-column prop="workstationName" label="工作站" show-overflow-tooltip />
        <el-table-column label="技能分" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">
            <el-rate v-model="row.skillScore" :max="5" show-score />
          </template>
        </el-table-column>
        <el-table-column label="主技能" min-width="100" show-overflow-tooltip>
          <template #default="{ row }">
            <el-switch v-model="row.isPrimarySkill" :active-value="1" :inactive-value="0" @change="onPrimaryChange(row, $event)" />
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <el-button @click="skillsVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingSkills" :disabled="savingSkills || skillsLoading" @click="handleSaveSkills">保存技能</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { createEmployee, deactivateEmployee, getEmployeeSkills, getEmployees, saveEmployeeSkills, updateEmployee } from '../api/employees'
import { getWorkstations } from '../api/workstations'
import { getLeaveReviewList } from '../api/leave'
import { getRules } from '../api/rules'
import { getStores } from '../api/store'

// 超管:跨全部门店查看员工(不含兼职),带门店筛选
// 按用户名判定(E001 等店长账号的数据库角色也是 SYSTEM_ADMIN)
const isSystemAdmin = computed(() => localStorage.getItem('shift_username') === 'admin')
const stores = ref([])

async function loadStores() {
  if (!isSystemAdmin.value) return
  try {
    stores.value = await getStores()
  } catch {
    stores.value = []
  }
}

const departments = ['管理', '行政', '工程', '保洁', '楼面', '厨房', '吧台']

const loading = ref(false)
const list = ref([])
const total = ref(0)
// 全局「最大周工时」默认值（来自规则配置），用于员工表单「跟随默认」展示
const globalMaxWeeklyHours = ref(60)
const query = reactive({
  page: 1,
  pageSize: 10,
  name: '',
  employeeNo: '',
  department: '',
  storeId: null
})

// P3-3: 搜索重置页码
function search() {
  query.page = 1
  loadData()
}

// P3-25: 请求序号防止旧响应覆盖
let requestSeq = 0

async function loadData() {
  const seq = ++requestSeq
  loading.value = true
  // 请假信息与分页无关，后台并行刷新（失败不影响员工列表）
  loadLeaveMap()
  try {
    const res = await getEmployees({
      page: query.page,
      pageSize: query.pageSize,
      name: query.name || undefined,
      employeeNo: query.employeeNo || undefined,
      department: query.department || undefined,
      storeId: query.storeId || undefined
    })
    if (seq !== requestSeq) return
    list.value = res.items
    total.value = res.total
  } finally {
    if (seq === requestSeq) loading.value = false
  }
}

// 已批准请假：员工工号 → 请假时间段（仅保留今天及以后的休假，历史已结束的休假不显示）
const leaveMap = ref({})
async function loadLeaveMap() {
  // 请假审批列表仅店长账号可访问(后端按用户名放行);
  // admin 不请求,避免页面加载时弹出 403「无权限」
  const role = localStorage.getItem('shift_role') || ''
  if (role !== 'STORE_MANAGER') return
  try {
    const list = await getLeaveReviewList('APPROVED')
    const map = {}
    for (const l of list || []) {
      const no = l.employee?.employeeNo
      if (!no) continue
      if (!map[no]) map[no] = []
      map[no].push({ startDate: l.startDate, endDate: l.endDate, earlyReturned: Number(l.earlyReturned) === 1 })
    }
    for (const arr of Object.values(map)) {
      arr.sort((a, b) => (a.startDate < b.startDate ? -1 : 1))
    }
    leaveMap.value = map
  } catch {
    // 请假信息加载失败不影响员工列表
  }
}

function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 该员工的休假时间段（今天及以后）；短格式用于单元格,完整格式用于悬停
function leavePeriods(row) {
  const today = getToday()
  return (leaveMap.value[row.employeeNo] || [])
    .filter(l => l.endDate >= today)
    .map(l => ({
      shortNoSpace: `${l.startDate.slice(5)}~${l.endDate.slice(5)}`,
      full: `${l.startDate} ~ ${l.endDate}`,
      earlyReturned: l.earlyReturned
    }))
}

// 今天是否正处于休假中
function onLeaveNow(row) {
  const today = getToday()
  return (leaveMap.value[row.employeeNo] || []).some(l => l.startDate <= today && l.endDate >= today)
}

function handlePageChange(page) {
  query.page = page
  loadData()
}

const dialogVisible = ref(false)
const editing = ref(false)
const saving = ref(false)
const deactivating = ref(false)
const formRef = ref()
const form = reactive({
  employeeNo: '',
  name: '',
  phone: '',
  storeId: null,
  department: '',
  primaryPosition: '',
  maxWeeklyHours: 48,
  weeklyHoursFollowDefault: 1
})
const rules = computed(() => ({
  employeeNo: [{ required: true, message: '请输入工号', trigger: 'blur' }],
  name: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
  department: [{ required: true, message: '请选择部门', trigger: 'change' }],
  storeId: isSystemAdmin.value ? [{ required: true, message: '请选择门店', trigger: 'change' }] : [],
  maxWeeklyHours: [{ required: true, message: '请输入周工时上限', trigger: 'blur' }]
}))

// P3-28: 重置表单并清除验证状态
function resetForm() {
  form.employeeNo = ''
  form.name = ''
  form.phone = ''
  form.storeId = null
  form.department = ''
  form.primaryPosition = ''
  form.maxWeeklyHours = globalMaxWeeklyHours.value
  form.weeklyHoursFollowDefault = 1
  editing.value = false
  nextTick(() => {
    formRef.value?.clearValidate?.()
  })
}

function openCreate() {
  resetForm()
  dialogVisible.value = true
}

function openEdit(row) {
  editing.value = true
  currentEmployeeId.value = row.id
  form.employeeNo = row.employeeNo
  form.name = row.name
  form.phone = row.phone || ''
  form.storeId = row.storeId ?? null
  form.department = row.department
  form.primaryPosition = row.primaryPosition || ''
  form.maxWeeklyHours = Number(row.maxWeeklyHours)
  form.weeklyHoursFollowDefault = Number(row.weeklyHoursFollowDefault ?? 1)
  dialogVisible.value = true
  nextTick(() => {
    formRef.value?.clearValidate?.()
  })
}

// P3-40: 弹窗取消/验证失败不执行保存
async function handleSave() {
  if (saving.value) return
  try {
    await formRef.value.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    if (editing.value) {
      await updateEmployee(currentEmployeeId.value, form)
      ElMessage.success('编辑成功')
    } else {
      await createEmployee(form)
      ElMessage.success('新增成功')
    }
    dialogVisible.value = false
    loadData()
  } catch (e) {
    ElMessage.error('保存失败：' + (e.message || '网络错误'))
  } finally {
    saving.value = false
  }
}

// P3-40: 取消确认后不执行停用
async function handleDeactivate(row) {
  if (deactivating.value) return
  try {
    await ElMessageBox.confirm('确定停用员工 ' + row.name + ' 吗？', '提示', { type: 'warning' })
  } catch {
    return
  }
  deactivating.value = true
  try {
    await deactivateEmployee(row.id)
    ElMessage.success('已停用')
    loadData()
  } catch (e) {
    ElMessage.error('停用失败：' + (e.message || '网络错误'))
  } finally {
    deactivating.value = false
  }
}

const skillsVisible = ref(false)
const skillsLoading = ref(false)
const savingSkills = ref(false)
const skillRows = ref([])
const currentEmployee = ref(null)
const currentEmployeeId = ref(0)

// 请求序号：防止快速切换员工时慢响应覆盖
let skillsSeq = 0

async function openSkills(row) {
  const seq = ++skillsSeq
  currentEmployee.value = row
  currentEmployeeId.value = row.id
  skillsVisible.value = true
  skillsLoading.value = true
  try {
    const matrix = await getEmployeeSkills(row.id)
    const workstations = await getWorkstations()
    if (seq !== skillsSeq) return
    skillRows.value = workstations.map(ws => {
      const existing = (matrix?.skills || []).find(s => s.workstationId === ws.id)
      return {
        workstationId: ws.id,
        workstationName: ws.name,
        skillScore: existing?.skillScore ?? 0,
        isPrimarySkill: existing?.isPrimarySkill ?? 0
      }
    })
  } catch (e) {
    if (seq === skillsSeq) {
      skillRows.value = []
      ElMessage.error('加载技能失败')
    }
  } finally {
    if (seq === skillsSeq) {
      skillsLoading.value = false
    }
  }
}

// P3-29: 主技能唯一
function onPrimaryChange(row, isPrimary) {
  if (isPrimary) {
    skillRows.value.forEach(r => {
      if (r.workstationId !== row.workstationId) {
        r.isPrimarySkill = 0
      }
    })
  }
}

// P3-40: 技能保存失败不静默
async function handleSaveSkills() {
  if (savingSkills.value) return
  savingSkills.value = true
  try {
    await saveEmployeeSkills(currentEmployeeId.value, {
      skills: skillRows.value.map(r => ({
        workstationId: r.workstationId,
        skillScore: r.skillScore,
        isPrimarySkill: r.isPrimarySkill
      }))
    })
    ElMessage.success('技能保存成功')
    skillsVisible.value = false
  } catch (e) {
    ElMessage.error('技能保存失败：' + (e.message || '网络错误'))
  } finally {
    savingSkills.value = false
  }
}

// 加载全局「最大周工时」默认值
async function loadGlobalMaxWeeklyHours() {
  try {
    const rules = await getRules()
    const rule = (rules || []).find(r => r.ruleKey === 'max_weekly_hours')
    if (rule && !Number.isNaN(Number(rule.ruleValue))) {
      globalMaxWeeklyHours.value = Number(rule.ruleValue)
    }
  } catch {
    // 加载失败沿用默认 60，不影响员工列表
  }
}

onMounted(() => {
  loadStores()
  loadData()
  loadGlobalMaxWeeklyHours()
})
</script>

<style scoped>
/* 表单标签永远单行(「周工时上限」等 5 字标签不换行) */
.dialog-form :deep(.el-form-item__label) {
  white-space: nowrap;
}
.follow-hint {
  margin-left: var(--app-space-4);
  color: var(--el-text-color-secondary);
  font-size: var(--app-font-sm);
}
.leave-line {
  font-size: var(--app-font-sm);
  line-height: 1.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--el-text-color-regular);
}
/* 提前返岗的请假:橙色文字提示,详情在悬停标题 */
.leave-early {
  color: var(--el-color-warning);
  font-weight: 600;
}
.early-return-tag {
  margin-right: var(--app-space-2);
  vertical-align: middle;
}
</style>