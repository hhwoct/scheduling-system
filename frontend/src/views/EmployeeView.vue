<template>
  <div>
    <el-card>
      <el-form inline :model="query">
        <el-form-item label="姓名">
          <el-input v-model="query.name" placeholder="姓名" clearable style="width: 160px" @keyup.enter="loadData" />
        </el-form-item>
        <el-form-item label="工号">
          <el-input v-model="query.employeeNo" placeholder="工号" clearable style="width: 160px" @keyup.enter="loadData" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="query.department" placeholder="全部" clearable style="width: 140px">
            <el-option v-for="d in departments" :key="d" :label="d" :value="d" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadData">查询</el-button>
        </el-form-item>
      </el-form>

      <el-button type="primary" style="margin-bottom: 12px" @click="openCreate">新增员工</el-button>

      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="employeeNo" label="工号" width="100" />
        <el-table-column prop="name" label="姓名" width="120" />
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
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="openSkills(row)">技能</el-button>
            <el-button v-if="row.status === 1" link type="danger" @click="handleDeactivate(row)">停用</el-button>
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
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
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
            <el-switch v-model="row.isPrimarySkill" :active-value="1" :inactive-value="0" />
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <el-button @click="skillsVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingSkills" @click="handleSaveSkills">保存技能</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
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

async function loadData() {
  loading.value = true
  try {
    const res = await getEmployees({
      page: query.page,
      pageSize: query.pageSize,
      name: query.name || undefined,
      employeeNo: query.employeeNo || undefined,
      department: query.department || undefined
    })
    list.value = res.items
    total.value = res.total
  } finally {
    loading.value = false
  }
}

function handlePageChange(page) {
  query.page = page
  loadData()
}

const dialogVisible = ref(false)
const editing = ref(false)
const saving = ref(false)
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

function resetForm() {
  form.employeeNo = ''
  form.name = ''
  form.phone = ''
  form.department = ''
  form.primaryPosition = ''
  form.maxWeeklyHours = 48
  editing.value = false
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
}

async function handleSave() {
  await formRef.value.validate()
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
  } finally {
    saving.value = false
  }
}

async function handleDeactivate(row) {
  await ElMessageBox.confirm('确定停用员工 ' + row.name + ' 吗？', '提示', { type: 'warning' })
  await deactivateEmployee(row.id)
  ElMessage.success('已停用')
  loadData()
}

const skillsVisible = ref(false)
const skillsLoading = ref(false)
const savingSkills = ref(false)
const skillRows = ref([])
const currentEmployee = ref(null)
const currentEmployeeId = ref(0)

async function openSkills(row) {
  currentEmployee.value = row
  currentEmployeeId.value = row.id
  skillsVisible.value = true
  skillsLoading.value = true
  try {
    const matrix = await getEmployeeSkills(row.id)
    const workstations = await getWorkstations()
    skillRows.value = workstations.map(ws => {
      const existing = matrix.skills.find(s => s.workstationId === ws.id)
      return {
        workstationId: ws.id,
        workstationName: ws.name,
        skillScore: existing?.skillScore ?? 0,
        isPrimarySkill: existing?.isPrimarySkill ?? 0
      }
    })
  } finally {
    skillsLoading.value = false
  }
}

async function handleSaveSkills() {
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
  } finally {
    savingSkills.value = false
  }
}

onMounted(loadData)
</script>
