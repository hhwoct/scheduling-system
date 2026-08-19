<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>高峰禁休时段</span>
          <el-button type="primary" @click="openCreate">新增时段</el-button>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="员工班中休息（30 分钟）不会安排在这些时段内，休息与高峰时段完全不重叠。"
        style="margin-bottom: 12px"
      />

      <el-table :data="list" v-loading="loading" border stripe>
        <el-table-column label="开始时间" width="160">
          <template #default="{ row }">{{ fmtTime(row.startTime) }}</template>
        </el-table-column>
        <el-table-column label="结束时间" width="160">
          <template #default="{ row }">{{ fmtTime(row.endTime) }}</template>
        </el-table-column>
        <el-table-column label="启用" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.status === 1" type="success" size="small">启用</el-tag>
            <el-tag v-else type="info" size="small">停用</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="180">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" :disabled="deleting" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-dialog v-model="dialogVisible" :title="editingId ? '编辑高峰时段' : '新增高峰时段'" width="440px">
        <el-form label-width="90px">
          <el-form-item label="开始时间">
            <el-time-picker
              v-model="form.startTime"
              format="HH:mm"
              value-format="HH:mm"
              placeholder="开始时间"
              :disabled-minutes="disabledMinutes"
              style="width: 100%"
            />
          </el-form-item>
          <el-form-item label="结束时间">
            <el-time-picker
              v-model="form.endTime"
              format="HH:mm"
              value-format="HH:mm"
              placeholder="结束时间"
              :disabled-minutes="disabledMinutes"
              style="width: 100%"
            />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="form.status" :active-value="1" :inactive-value="0" />
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="dialogVisible = false">取消</el-button>
          <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
        </template>
      </el-dialog>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getPeakHours, createPeakHour, updatePeakHour, deletePeakHour } from '../api/peakHours'

const loading = ref(false)
const saving = ref(false)
const deleting = ref(false)
const list = ref([])
const dialogVisible = ref(false)
const editingId = ref(null)
const form = ref({ startTime: '20:00', endTime: '22:00', status: 1 })

function fmtTime(t) {
  if (!t) return '--'
  const m = String(t).match(/^(\d{2}):(\d{2})/)
  return m ? m[0] : '--'
}

// 只允许 0 分与 30 分（与算法时段粒度一致）
function disabledMinutes() {
  return Array.from({ length: 60 }, (_, i) => i).filter(m => m !== 0 && m !== 30)
}

async function loadData() {
  loading.value = true
  try {
    list.value = await getPeakHours()
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  form.value = { startTime: '20:00', endTime: '22:00', status: 1 }
  dialogVisible.value = true
}

function openEdit(row) {
  editingId.value = row.id
  form.value = { startTime: fmtTime(row.startTime), endTime: fmtTime(row.endTime), status: row.status }
  dialogVisible.value = true
}

async function handleSave() {
  if (!form.value.startTime || !form.value.endTime) {
    ElMessage.warning('请选择开始和结束时间')
    return
  }
  saving.value = true
  try {
    if (editingId.value) {
      await updatePeakHour(editingId.value, form.value)
      ElMessage.success('修改成功')
    } else {
      await createPeakHour(form.value)
      ElMessage.success('新增成功')
    }
    dialogVisible.value = false
    await loadData()
  } finally {
    saving.value = false
  }
}

async function handleDelete(row) {
  if (deleting.value) return
  try {
    await ElMessageBox.confirm(`确定删除高峰时段 ${fmtTime(row.startTime)}-${fmtTime(row.endTime)} 吗？`, '提示', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  deleting.value = true
  try {
    await deletePeakHour(row.id)
    ElMessage.success('删除成功')
    await loadData()
  } catch (e) {
    ElMessage.error('删除失败：' + (e?.message || '网络错误'))
  } finally {
    deleting.value = false
  }
}

onMounted(loadData)
</script>
