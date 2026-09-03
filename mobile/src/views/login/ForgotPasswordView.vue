<template>
  <div class="forgot-page">
    <van-nav-bar title="重置密码" left-arrow @click-left="router.back()" />

    <div class="forgot-tip">
      输入员工姓名与注册手机号验证身份后，可直接设置新密码。
    </div>

    <van-form @submit="onSubmit">
      <van-cell-group inset>
        <van-field
          v-model.trim="form.name"
          name="name"
          label="姓名"
          placeholder="请输入员工姓名"
          :rules="[{ required: true, message: '请输入员工姓名' }]"
          clearable
        />
        <van-field
          v-model.trim="form.verifyInfo"
          type="tel"
          name="verifyInfo"
          label="手机号"
          placeholder="请输入注册手机号"
          maxlength="11"
          :rules="[
            { required: true, message: '请输入注册手机号' },
            { pattern: /^\d{11}$/, message: '手机号需为 11 位数字' }
          ]"
          clearable
        />
        <van-field
          v-model="form.newPassword"
          type="password"
          name="newPassword"
          label="新密码"
          placeholder="至少 8 位，含大小写字母和数字"
          :rules="[
            { required: true, message: '请输入新密码' },
            { validator: validatePassword, message: '密码长度不能少于 8 位，且需包含大写字母、小写字母和数字' }
          ]"
        />
        <van-field
          v-model="form.confirmPassword"
          type="password"
          name="confirmPassword"
          label="确认密码"
          placeholder="再次输入新密码"
          :rules="[{ required: true, message: '请再次输入新密码' }]"
        />
      </van-cell-group>

      <div class="login-actions">
        <van-button round block type="primary" native-type="submit" :loading="loading">
          重置密码
        </van-button>
      </div>
    </van-form>
  </div>
</template>

<script setup>
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { showSuccessToast, showToast } from 'vant'
import { forgotPassword } from '../../api/auth'

const router = useRouter()

const form = reactive({ name: '', verifyInfo: '', newPassword: '', confirmPassword: '' })
const loading = ref(false)

function validatePassword(value) {
  if (!value) return false
  if (value.length < 8) return false
  return /[A-Z]/.test(value) && /[a-z]/.test(value) && /\d/.test(value)
}

async function onSubmit() {
  if (form.newPassword !== form.confirmPassword) {
    showToast('两次输入的密码不一致')
    return
  }
  loading.value = true
  try {
    await forgotPassword({
      name: form.name,
      verifyInfo: form.verifyInfo,
      newPassword: form.newPassword,
      confirmPassword: form.confirmPassword
    })
    showSuccessToast('密码重置成功，请使用新密码登录')
    router.replace('/login')
  } catch (e) {
    // 错误提示已由 request 拦截器统一弹出
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.forgot-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.forgot-tip {
  margin: 12px 16px;
  font-size: 13px;
  color: #969799;
  line-height: 1.6;
}
</style>
