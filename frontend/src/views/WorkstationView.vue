<template>
  <div>
    <el-card>
      <div style="margin-bottom: var(--app-space-5); display: flex; gap: var(--app-space-4)">
        <el-button type="primary" @click="openCreate">新增工作站</el-button>
        <el-button @click="loadData">刷新</el-button>
      </div>
      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column prop="code" label="编码" min-width="140" show-overflow-tooltip />
        <el-table-column prop="name" label="名称" min-width="120" />
        <el-table-column prop="sortOrder" label="排序" width="80" />
        <el-table-column label="低技能" width="90">
          <template #default="{ row }">
            <el-tag v-if="row.isLowSkill === 1" type="success" size="small">可兼职</el-tag>
            <el-tag v-else type="info" size="small">否</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'">{{ row.status === 1 ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 编辑弹窗 -->
    <el-dialog v-model="dialogVisible" title="编辑工作站" width="450px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" type="textarea" />
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="form.status" :active-value="1" :inactive-value="0" active-text="启用" inactive-text="停用" />
        </el-form-item>
        <el-form-item label="低技能岗位">
          <el-switch v-model="form.isLowSkill" :active-value="1" :inactive-value="0" active-text="可兼职填补" inactive-text="否" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 新增弹窗 -->
    <el-dialog v-model="createVisible" title="新增工作站" width="450px">
      <el-form :model="createForm" label-width="80px">
        <el-form-item label="编码" required>
          <el-input v-model="createForm.code" placeholder="如 EXTERNAL_BAR" />
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="createForm.name" placeholder="如 外吧岗" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="createForm.sortOrder" :min="0" :max="999" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="createForm.remark" type="textarea" />
        </el-form-item>
        <el-form-item label="低技能岗位">
          <el-switch v-model="createForm.isLowSkill" :active-value="1" :inactive-value="0" active-text="可兼职填补" inactive-text="否" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="creating" @click="handleCreate">新增</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getWorkstations, updateWorkstation, createWorkstation } from '../api/workstations'

const loading = ref(false)
const list = ref([])

// 编辑
const dialogVisible = ref(false)
const saving = ref(false)
const editingId = ref(0)
const form = reactive({ name: '', remark: '', status: 1, isLowSkill: 0 })

// 新增
const createVisible = ref(false)
const creating = ref(false)
const createForm = reactive({ code: '', name: '', sortOrder: 0, remark: '', isLowSkill: 0 })

async function loadData() {
  loading.value = true
  try {
    list.value = await getWorkstations({ includeInactive: true })
  } finally {
    loading.value = false
  }
}

function openCreate() {
  createForm.code = ''
  createForm.name = ''
  createForm.sortOrder = 0
  createForm.remark = ''
  createForm.isLowSkill = 0
  createVisible.value = true
}

async function handleCreate() {
  if (!createForm.code.trim() || !createForm.name.trim()) {
    ElMessage.warning('编码和名称不能为空')
    return
  }
  creating.value = true
  try {
    await createWorkstation({ ...createForm, code: createForm.code.trim(), name: createForm.name.trim() })
    ElMessage.success('新增成功')
    createVisible.value = false
    loadData()
  } finally {
    creating.value = false
  }
}

function openEdit(row) {
  editingId.value = row.id
  form.name = row.name
  form.remark = row.remark || ''
  form.status = row.status
  form.isLowSkill = row.isLowSkill ?? 0
  dialogVisible.value = true
}

async function handleSave() {
  saving.value = true
  try {
    await updateWorkstation(editingId.value, { ...form })
    ElMessage.success('保存成功')
    dialogVisible.value = false
    loadData()
  } finally {
    saving.value = false
  }
}

onMounted(loadData)
</script>