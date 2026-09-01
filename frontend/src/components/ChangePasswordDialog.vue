<template>
  <el-dialog v-model="visible" title="修改密码" width="420px" destroy-on-close>
    <el-form ref="formRef" :model="form" :rules="rules" label-width="100px" class="change-pwd-form">
      <el-form-item label="当前密码" prop="oldPassword">
        <el-input v-model="form.oldPassword" type="password" show-password placeholder="请输入当前密码" autocomplete="current-password" />
      </el-form-item>
      <el-form-item label="手机号" prop="verifyInfo">
        <el-input v-model="form.verifyInfo" placeholder="请输入注册手机号" maxlength="11" />
      </el-form-item>
      <el-form-item label="新密码" prop="newPassword">
        <el-input v-model="form.newPassword" type="password" show-password placeholder="至少 8 位，含大小写字母和数字" autocomplete="new-password" />
      </el-form-item>
      <el-form-item label="确认新密码" prop="confirmPassword">
        <el-input v-model="form.confirmPassword" type="password" show-password placeholder="再次输入新密码" autocomplete="new-password" />
      </el-form-item>
    </el-form>
    <div style="font-size: 12px; color: var(--el-text-color-secondary)">手机号需与员工档案中的注册手机号一致（管理员账号无档案，不校验）。修改成功后当前登录立即失效，需要使用新密码重新登录。</div>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="saving" @click="submit">确定修改</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { changePassword } from '../api/auth'
import { useAuthStore } from '../stores/auth'
import router from '../router'

const visible = defineModel({ type: Boolean, default: false })
const emit = defineEmits(['success'])

const formRef = ref(null)
const saving = ref(false)
const form = reactive({ oldPassword: '', verifyInfo: '', newPassword: '', confirmPassword: '' })

const rules = {
  oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  verifyInfo: [
    { required: true, message: '请输入注册手机号', trigger: 'blur' },
    { pattern: /^\d{11}$/, message: '手机号需为 11 位数字', trigger: 'blur' }
  ],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 8, message: '新密码至少 8 位', trigger: 'blur' },
    {
      validator: (rule, value, callback) => {
        if (!/[A-Z]/.test(value) || !/[a-z]/.test(value) || !/[0-9]/.test(value)) {
          callback(new Error('必须包含大写字母、小写字母和数字'))
        } else {
          callback()
        }
      },
      trigger: 'blur'
    }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    {
      validator: (rule, value, callback) => {
        if (value !== form.newPassword) {
          callback(new Error('两次输入的新密码不一致'))
        } else {
          callback()
        }
      },
      trigger: 'blur'
    }
  ]
}

// 关闭时重置表单，避免下次打开残留
watch(visible, (v) => {
  if (!v) {
    form.oldPassword = ''
    form.verifyInfo = ''
    form.newPassword = ''
    form.confirmPassword = ''
    formRef.value?.clearValidate()
  }
})

async function submit() {
  try {
    await formRef.value.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    await changePassword({
      oldPassword: form.oldPassword,
      verifyInfo: form.verifyInfo,
      newPassword: form.newPassword,
      confirmPassword: form.confirmPassword
    })
    ElMessage.success('密码修改成功，请重新登录')
    visible.value = false
    const authStore = useAuthStore()
    authStore.logout()
    router.push('/login')
    emit('success')
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
/* 标签固定单行显示，避免「确认新密码」五个字换行 */
.change-pwd-form :deep(.el-form-item__label) {
  white-space: nowrap;
}
</style>
