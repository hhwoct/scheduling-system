<template>
  <div class="login-page">
    <el-card class="login-card">
      <template #header>
        <div class="login-title">排班系统管理端</div>
      </template>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="0" size="large">
        <el-form-item prop="username">
          <el-input v-model="form.username" placeholder="用户名" :prefix-icon="User" />
        </el-form-item>
        <el-form-item prop="password">
          <el-input
            v-model="form.password"
            type="password"
            placeholder="密码"
            show-password
            :prefix-icon="Lock"
            @keyup.enter="handleLogin"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" class="login-btn" :loading="loading" :disabled="cooldown > 0" @click="handleLogin">
            {{ cooldown > 0 ? `${cooldown}s 后可重试` : '登录' }}
          </el-button>
        </el-form-item>
        <div v-if="cooldown > 0" class="cooldown-tip">登录失败次数过多，请稍候再试</div>
        <div class="forgot-password">
          <el-link type="primary" :underline="false" @click="openForgot">忘记密码？</el-link>
        </div>
      </el-form>
    </el-card>

    <!-- 忘记密码弹窗 -->
    <el-dialog v-model="forgotVisible" title="重置密码" width="420px">
      <el-form ref="forgotFormRef" :model="forgotForm" :rules="forgotRules" label-width="90px">
        <el-form-item label="姓名" prop="name">
          <el-input v-model="forgotForm.name" placeholder="请输入员工姓名（如 张店长）" />
        </el-form-item>
        <el-form-item label="手机号" prop="verifyInfo">
          <el-input v-model="forgotForm.verifyInfo" placeholder="请输入注册手机号" />
        </el-form-item>
        <el-form-item label="新密码" prop="newPassword">
          <el-input v-model="forgotForm.newPassword" type="password" show-password placeholder="至少 8 位，含大小写和数字" />
        </el-form-item>
        <el-form-item label="确认密码" prop="confirmPassword">
          <el-input v-model="forgotForm.confirmPassword" type="password" show-password placeholder="再次输入新密码" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="forgotVisible = false">取消</el-button>
        <el-button type="primary" :loading="forgotLoading" @click="handleForgotPassword">确认重置</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onBeforeUnmount, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'
import { forgotPassword } from '../api/auth'

const router = useRouter()
const authStore = useAuthStore()
const formRef = ref()
const loading = ref(false)

const form = reactive({
  username: '',
  password: ''
})

const rules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }]
}

// 登录失败冷却：防止回车键卡住/连发导致瞬间刷满失败次数触发锁定
const cooldown = ref(0)
let cooldownTimer = null

function startCooldown(seconds = 5) {
  cooldown.value = seconds
  if (cooldownTimer) clearInterval(cooldownTimer)
  cooldownTimer = setInterval(() => {
    cooldown.value -= 1
    if (cooldown.value <= 0) {
      clearInterval(cooldownTimer)
      cooldownTimer = null
    }
  }, 1000)
}

onBeforeUnmount(() => {
  if (cooldownTimer) clearInterval(cooldownTimer)
})

async function handleLogin() {
  // 冷却期间忽略任何提交（含回车连发）
  if (loading.value || cooldown.value > 0) return
  try {
    await formRef.value.validate()
  } catch {
    return
  }
  loading.value = true
  try {
    await authStore.login(form.username, form.password)
    ElMessage.success('登录成功')
    router.push('/')
  } catch (e) {
    // 密码错误/账号锁定等：启动冷却，间隔重试
    startCooldown(5)
  } finally {
    loading.value = false
  }
}

const forgotVisible = ref(false)
const forgotLoading = ref(false)
const forgotFormRef = ref()
const forgotForm = reactive({
  name: '',
  verifyInfo: '',
  newPassword: '',
  confirmPassword: ''
})

const forgotRules = {
  name: [{ required: true, message: '请输入员工姓名', trigger: 'blur' }],
  verifyInfo: [
    { required: true, message: '请输入注册手机号', trigger: 'blur' },
    { pattern: /^\d{11}$/, message: '手机号需为 11 位数字', trigger: 'blur' }
  ],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    {
      validator: (rule, value, callback) => {
        if (!value) {
          callback(new Error('请输入新密码'))
        } else if (value.length < 8) {
          callback(new Error('密码长度不能少于 8 位'))
        } else if (!/[A-Z]/.test(value) || !/[a-z]/.test(value) || !/\d/.test(value)) {
          callback(new Error('密码必须包含大写字母、小写字母和数字'))
        } else {
          callback()
        }
      },
      trigger: 'blur'
    }
  ],
  confirmPassword: [{ required: true, message: '请再次输入新密码', trigger: 'blur' }]
}

function openForgot() {
  forgotForm.name = ''
  forgotForm.verifyInfo = ''
  forgotForm.newPassword = ''
  forgotForm.confirmPassword = ''
  forgotVisible.value = true
}

async function handleForgotPassword() {
  try {
    await forgotFormRef.value.validate()
  } catch {
    return // 校验失败，表单已标红
  }
  if (forgotForm.newPassword !== forgotForm.confirmPassword) {
    ElMessage.error('两次输入的密码不一致')
    return
  }
  forgotLoading.value = true
  try {
    await forgotPassword({
      name: forgotForm.name,
      verifyInfo: forgotForm.verifyInfo,
      newPassword: forgotForm.newPassword,
      confirmPassword: forgotForm.confirmPassword
    })
    ElMessage.success('密码重置成功，请使用新密码登录')
    forgotVisible.value = false
    form.username = ''
    form.password = ''
  } catch (e) {
    // 错误提示由响应拦截器统一处理
  } finally {
    forgotLoading.value = false
  }
}
</script>

<style scoped>
.login-page {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #001529 0%, #003a70 100%);
}
.login-card {
  width: 380px;
}
.login-title {
  text-align: center;
  font-size: 18px;
  font-weight: 600;
}
.login-btn {
  width: 100%;
}
.forgot-password {
  text-align: right;
  margin-top: -8px;
}
.cooldown-tip {
  margin-top: -4px;
  margin-bottom: 8px;
  font-size: 12px;
  color: var(--el-color-warning);
  text-align: center;
}
</style>