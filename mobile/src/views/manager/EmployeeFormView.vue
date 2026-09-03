<template>
  <div class="emp-form-page">
    <van-nav-bar :title="isEdit ? '编辑员工' : '新增员工'" left-arrow @click-left="router.back()" />

    <van-loading v-if="editLoading" class="page-loading" size="24" vertical>加载中…</van-loading>

    <van-form v-else @submit="onSubmit">
      <van-cell-group inset>
        <van-field
          v-model.trim="form.employeeNo"
          name="employeeNo"
          label="工号"
          placeholder="请输入工号"
          maxlength="20"
          :rules="[{ required: true, message: '请输入工号' }]"
        />
        <van-field
          v-model.trim="form.name"
          name="name"
          label="姓名"
          placeholder="请输入姓名"
          maxlength="30"
          :rules="[{ required: true, message: '请输入姓名' }]"
        />
        <van-field
          v-if="isAdmin && !isEdit"
          v-model="storeName"
          readonly
          is-link
          name="storeId"
          label="门店"
          placeholder="请选择门店"
          :rules="[{ required: true, message: '请选择门店' }]"
          @click="showStorePicker = true"
        />
        <van-field
          v-model="form.department"
          readonly
          is-link
          name="department"
          label="部门"
          placeholder="请选择部门"
          :rules="[{ required: true, message: '请选择部门' }]"
          @click="showDeptPicker = true"
        />
        <van-field
          v-model.trim="form.primaryPosition"
          name="primaryPosition"
          label="主岗"
          placeholder="选填，如 服务台/厨房"
          maxlength="30"
        />
        <van-field
          v-model.trim="form.phone"
          type="tel"
          name="phone"
          label="手机号"
          placeholder="选填，11 位手机号"
          :rules="[{ validator: validatePhone, message: '手机号需为 11 位数字' }]"
        />
      </van-cell-group>

      <div class="form-tip" v-if="isMaskedPhone">
        手机号为脱敏显示（****），不修改将保持原号码；如需修改请清空后输入完整 11 位手机号。
      </div>

      <van-cell-group inset class="hours-group">
        <van-cell title="跟随默认周工时上限" label="跟随全局配置，保存后自动取默认值">
          <template #right-icon>
            <van-switch v-model="form.weeklyHoursFollowDefault" :active-value="1" :inactive-value="0" size="22" />
          </template>
        </van-cell>
        <van-field name="maxWeeklyHours" label="周工时上限" :rules="[{ required: true, message: '请设置周工时上限' }]">
          <template #input>
            <van-stepper
              v-model="form.maxWeeklyHours"
              :min="1"
              :max="168"
              :disabled="form.weeklyHoursFollowDefault === 1"
              integer
            />
          </template>
        </van-field>
      </van-cell-group>

      <div class="form-actions">
        <van-button round block type="primary" native-type="submit" :loading="saving">
          {{ isEdit ? '保存修改' : '新增员工' }}
        </van-button>
      </div>
    </van-form>

    <van-popup v-model:show="showDeptPicker" position="bottom" round>
      <van-picker
        :columns="departments"
        title="选择部门"
        @confirm="onDeptConfirm"
        @cancel="showDeptPicker = false"
      />
    </van-popup>

    <van-popup v-model:show="showStorePicker" position="bottom" round>
      <van-picker
        :columns="storeColumns"
        title="选择门店"
        @confirm="onStoreConfirm"
        @cancel="showStorePicker = false"
      />
    </van-popup>
  </div>
</template>

<script setup>
import { reactive, ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { showSuccessToast } from 'vant'
import { getEmployeeDetail, createEmployee, updateEmployee } from '../../api/employees'
import { getStores } from '../../api/store'
import { useAuthStore } from '../../stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

// 超管新增员工时需选择归属门店（编辑时门店不可修改）
const isAdmin = computed(() => auth.effectiveRole === 'admin')

const departments = ['管理', '行政', '工程', '保洁', '楼面', '厨房', '吧台']

const empId = computed(() => {
  const id = Number(route.query.id)
  return Number.isFinite(id) && id > 0 ? id : 0
})
const isEdit = computed(() => empId.value > 0)

const form = reactive({
  employeeNo: '',
  name: '',
  department: '',
  primaryPosition: '',
  phone: '',
  weeklyHoursFollowDefault: 1,
  maxWeeklyHours: 60
})

const saving = ref(false)
const editLoading = ref(false)
const showDeptPicker = ref(false)

// 门店选择（仅超管新增员工时）
const stores = ref([])
const storeId = ref(null)
const storeName = ref('')
const showStorePicker = ref(false)

const storeColumns = computed(() =>
  stores.value.map((s) => ({ text: s.name, value: s.id }))
)

function onStoreConfirm({ selectedOptions }) {
  const s = selectedOptions?.[0]
  storeId.value = s ? Number(s.value) : null
  storeName.value = s?.text || ''
  showStorePicker.value = false
}

function onDeptConfirm({ selectedOptions }) {
  form.department = selectedOptions?.[0]?.text || ''
  showDeptPicker.value = false
}

// 当前是否为脱敏手机号（编辑回填时后端返回 138****0001 形式）
const isMaskedPhone = computed(() => !!form.phone && form.phone.includes('*'))

// 手机号校验：选填；脱敏值原样提交（后端保留原号码），否则需 11 位数字
function validatePhone(value) {
  if (!value) return true
  if (String(value).includes('*')) return true
  return /^\d{11}$/.test(String(value))
}

async function loadDetail() {
  if (!isEdit.value) return
  editLoading.value = true
  try {
    const d = await getEmployeeDetail(empId.value)
    form.employeeNo = d?.employeeNo || ''
    form.name = d?.name || ''
    form.department = d?.department || ''
    form.primaryPosition = d?.primaryPosition || ''
    form.phone = d?.phone || ''
    form.weeklyHoursFollowDefault = d?.weeklyHoursFollowDefault === 1 ? 1 : 0
    form.maxWeeklyHours = Number(d?.maxWeeklyHours) || 60
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    editLoading.value = false
  }
}

async function onSubmit() {
  saving.value = true
  const payload = {
    employeeNo: form.employeeNo,
    name: form.name,
    department: form.department,
    primaryPosition: form.primaryPosition || '',
    phone: form.phone || '',
    weeklyHoursFollowDefault: form.weeklyHoursFollowDefault === 1 ? 1 : 0,
    maxWeeklyHours: Number(form.maxWeeklyHours) || 60,
    storeId: isAdmin.value && !isEdit.value ? storeId.value : null
  }
  try {
    if (isEdit.value) {
      await updateEmployee(empId.value, payload)
      showSuccessToast('修改成功')
    } else {
      await createEmployee(payload)
      showSuccessToast('新增成功')
    }
    router.replace(isAdmin.value ? '/admin/employees' : '/manager/employees')
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  await loadDetail()
  if (isAdmin.value) {
    try {
      stores.value = (await getStores()) || []
    } catch (e) {
      stores.value = []
    }
  }
})
</script>

<style scoped>
.emp-form-page {
  min-height: 100vh;
  background: #f2f2f7;
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

.hours-group {
  margin-top: 12px;
}

.form-tip {
  margin: 8px 20px 0;
  font-size: 12px;
  color: #8e8e93;
  line-height: 1.7;
}

.form-actions {
  margin: 24px 16px;
}
</style>
