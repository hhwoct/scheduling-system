<template>
  <div>
    <el-card>
      <el-form inline :model="query">
        <el-form-item label="姓名">
          <el-input v-model="query.name" placeholder="姓名" clearable style="width: 160px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="工号">
          <el-input v-model="query.employeeNo" placeholder="工号" clearable style="width: 160px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="query.department" placeholder="全部" clearable style="width: 140px" @change="search">
            <el-option v-for="d in departments" :key="d" :label="d" :value="d" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
        </el-form-item>
      </el-form>

      <el-button type="primary" style="margin-bottom: 12px" @click="openCreate">新增员工</el-button>

      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="employeeNo" label="工号" width="100" />
        <el-table-column label="姓名" width="140">
          <template #default="{ row }">{{ row.name }}<el-tag v-if="row.isParttime === 1" type="warning" size="small" style="margin-left:4px">兼</el-tag></template>
        </el-table-column>
        <el-table-column prop="department" label="部门" width="100" />
        <el-table-column prop="primaryPosition" label="主岗" />
        <el-table-column prop="phone" label="手机号" width="140" />
        <el-table-column prop="maxWeeklyHours" label="周工时上限" width="100" />
        <el-table-column label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'">{{ row.status === 1 ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <el-button :link="true" type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button :link="true" type="primary" @click="openSkills(row)">技能</el-button>
            <el-button v-if="row.status === 1" :link="true" type="danger" :disabled="deactivating" @click="handleDeactivate(row)">停用</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        style="margin-top: 16px"
        layout="total, prev, pager, next"
        :total="total"
        :page-size="query.pageSize"
        :current-page="query.page"
        @current-change="handlePageChange"
      />
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑员工' : '新增员工'" width="500px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <el-form-item label="工号" prop="employeeNo">
          <el-input v-model="form.employeeNo" />
        </el-form-item>
        <el-form-item label="姓名" prop="name">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="部门" prop="department">
          <el-select v-model="form.department" style="width: 100%">
            <el-option v-for="d in departments" :key="d" :label="d" :value="d" />
          </el-select>
        </el-form-item>
        <el-form-item label="主岗" prop="primaryPosition">
          <el-input v-model="form.primaryPosition" />
        </el-form-item>
        <el-form-item label="手机号">
          <el-input v-model="form.phone" />
        </el-form-item>
        <el-form-item label="周工时上限" prop="maxWeeklyHours">
          <el-input-number v-model="form.maxWeeklyHours" :min="1" :max="168" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" :disabled="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="skillsVisible" :title="'技能配置 - ' + (currentEmployee?.name || '')" width="600px">
      <el-table :data="skillRows" v-loading="skillsLoading" border stripe size="small" max-height="480">
        <el-table-column prop="workstationName" label="工作站" />
        <el-table-column label="技能分" width="220">
          <template #default="{ row }">
            <el-rate v-model="row.skillScore" :max="5" show-score />
          </template>
        </el-table-column>
        <el-table-column label="主技能" width="100">
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
import { nextTick, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { createEmployee, deactivateEmployee, getEmployeeSkills, getEmployees, saveEmployeeSkills, updateEmployee } from '../api/employees'
import { getWorkstations } from '../api/workstations'

const departments = ['管理', '行政', '工程', '保洁', '楼面', '厨房', '吧台']

const loading = ref(false)
const list = ref([])
const total = ref(0)
const query = reactive({
  page: 1,
  pageSize: 10,
  name: '',
  employeeNo: '',
  department: ''
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
  try {
    const res = await getEmployees({
      page: query.page,
      pageSize: query.pageSize,
      name: query.name || undefined,
      employeeNo: query.employeeNo || undefined,
      department: query.department || undefined
    })
    if (seq !== requestSeq) return
    list.value = res.items
    total.value = res.total
  } finally {
    if (seq === requestSeq) loading.value = false
  }
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
  department: '',
  primaryPosition: '',
  maxWeeklyHours: 48
})
const rules = {
  employeeNo: [{ required: true, message: '请输入工号', trigger: 'blur' }],
  name: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
  department: [{ required: true, message: '请选择部门', trigger: 'change' }],
  maxWeeklyHours: [{ required: true, message: '请输入周工时上限', trigger: 'blur' }]
}

// P3-28: 重置表单并清除验证状态
function resetForm() {
  form.employeeNo = ''
  form.name = ''
  form.phone = ''
  form.department = ''
  form.primaryPosition = ''
  form.maxWeeklyHours = 48
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
  form.department = row.department
  form.primaryPosition = row.primaryPosition || ''
  form.maxWeeklyHours = Number(row.maxWeeklyHours)
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
      const existing = matrix.skills.find(s => s.workstationId === ws.id)
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

onMounted(loadData)
</script>