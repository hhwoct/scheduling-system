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
          <el-button type="primary" class="login-btn" :loading="loading" @click="handleLogin">
            登录
          </el-button>
        </el-form-item>
        <div class="forgot-password">
          <el-link type="primary" :underline="false" @click="openForgot">忘记密码？</el-link>
        </div>
      </el-form>
    </el-card>

    <!-- 忘记密码弹窗 -->
    <el-dialog v-model="forgotVisible" title="重置密码" width="420px">
      <el-form ref="forgotFormRef" :model="forgotForm" :rules="forgotRules" label-width="90px">
        <el-form-item label="用户名" prop="username">
          <el-input v-model="forgotForm.username" placeholder="请输入用户名" />
        </el-form-item>
        <el-form-item label="手机号" prop="verifyInfo">
          <el-input v-model="forgotForm.verifyInfo" placeholder="请输入注册手机号" />
        </el-form-item>
        <el-form-item label="验证码" prop="otpCode">
          <div class="otp-row">
            <el-input v-model="forgotForm.otpCode" placeholder="6 位验证码" maxlength="6" />
            <el-button :disabled="otpCountdown > 0" :loading="otpSending" @click="sendOtp">
              {{ otpCountdown > 0 ? otpCountdown + 's' : '获取验证码' }}
            </el-button>
          </div>
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
import { onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'
import { forgotPassword, sendResetOtp } from '../api/auth'

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

async function handleLogin() {
  await formRef.value.validate()
  loading.value = true
  try {
    await authStore.login(form.username, form.password)
    ElMessage.success('登录成功')
    router.push('/')
  } finally {
    loading.value = false
  }
}

const forgotVisible = ref(false)
const forgotLoading = ref(false)
const forgotFormRef = ref()
const forgotForm = reactive({
  username: '',
  verifyInfo: '',
  otpCode: '',
  newPassword: '',
  confirmPassword: ''
})

// 验证码发送状态与倒计时
const otpSending = ref(false)
const otpCountdown = ref(0)
let otpTimer = null

function stopOtpTimer() {
  if (otpTimer) {
    clearInterval(otpTimer)
    otpTimer = null
  }
  otpCountdown.value = 0
}

// 用户名变化后旧验证码作废（OTP 按用户名+手机号绑定）
watch(() => forgotForm.username, () => {
  forgotForm.otpCode = ''
})

// 关闭弹窗时停止倒计时
watch(forgotVisible, (visible) => {
  if (!visible) stopOtpTimer()
})

onBeforeUnmount(stopOtpTimer)

async function sendOtp() {
  if (!forgotForm.username.trim()) {
    ElMessage.warning('请先输入用户名')
    return
  }
  otpSending.value = true
  try {
    await sendResetOtp({ username: forgotForm.username.trim() })
    ElMessage.success('验证码已发送，10 分钟内有效')
    stopOtpTimer()
    otpCountdown.value = 60
    otpTimer = setInterval(() => {
      otpCountdown.value -= 1
      if (otpCountdown.value <= 0) stopOtpTimer()
    }, 1000)
  } catch (e) {
    // 错误提示由响应拦截器统一处理
  } finally {
    otpSending.value = false
  }
}

const forgotRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  verifyInfo: [{ required: true, message: '请输入注册手机号', trigger: 'blur' }],
  otpCode: [
    { required: true, message: '请输入验证码', trigger: 'blur' },
    { pattern: /^\d{6}$/, message: '验证码为 6 位数字', trigger: 'blur' }
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
  forgotForm.username = form.username || ''
  forgotForm.verifyInfo = ''
  forgotForm.otpCode = ''
  forgotForm.newPassword = ''
  forgotForm.confirmPassword = ''
  stopOtpTimer()
  forgotVisible.value = true
}

async function handleForgotPassword() {
  await forgotFormRef.value.validate()
  if (forgotForm.newPassword !== forgotForm.confirmPassword) {
    ElMessage.error('两次输入的密码不一致')
    return
  }
  forgotLoading.value = true
  try {
    await forgotPassword({
      username: forgotForm.username,
      verifyInfo: forgotForm.verifyInfo,
      otpCode: forgotForm.otpCode.trim(),
      newPassword: forgotForm.newPassword,
      confirmPassword: forgotForm.confirmPassword
    })
    ElMessage.success('密码重置成功，请使用新密码登录')
    forgotVisible.value = false
    form.username = forgotForm.username
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
.otp-row {
  display: flex;
  gap: 8px;
  width: 100%;
}
.otp-row .el-button {
  flex-shrink: 0;
}
</style>