<template>
  <div class="chgpwd-page">
    <van-nav-bar title="修改密码" left-arrow @click-left="router.back()" />

    <van-form @submit="onSubmit">
      <van-cell-group inset>
        <van-field
          v-model="form.oldPassword"
          type="password"
          name="oldPassword"
          label="当前密码"
          placeholder="请输入当前密码"
          autocomplete="current-password"
          :rules="[{ required: true, message: '请输入当前密码' }]"
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
        />
        <van-field
          v-model="form.newPassword"
          type="password"
          name="newPassword"
          label="新密码"
          placeholder="至少 8 位，含大小写字母和数字"
          autocomplete="new-password"
          :rules="[
            { required: true, message: '请输入新密码' },
            {
              validator: validatePassword,
              message: '密码至少 8 位，且需包含大写字母、小写字母和数字'
            }
          ]"
        />
        <van-field
          v-model="form.confirmPassword"
          type="password"
          name="confirmPassword"
          label="确认新密码"
          placeholder="再次输入新密码"
          autocomplete="new-password"
          :rules="[{ required: true, message: '请再次输入新密码' }]"
        />
      </van-cell-group>

      <div class="form-actions">
        <van-button round block type="primary" native-type="submit" :loading="submitting">
          确定修改
        </van-button>
      </div>

      <div class="form-tip">
        手机号需与员工档案中的注册手机号一致（管理员账号无档案，不校验）。修改成功后当前登录立即失效，需要使用新密码重新登录。
      </div>
    </van-form>
  </div>
</template>

<script setup>
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { showToast, showSuccessToast } from 'vant'
import { changePassword } from '../../api/auth'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const auth = useAuthStore()

const form = reactive({ oldPassword: '', verifyInfo: '', newPassword: '', confirmPassword: '' })
const submitting = ref(false)

function validatePassword(value) {
  if (!value) return false
  if (value.length < 8) return false
  return /[A-Z]/.test(value) && /[a-z]/.test(value) && /\d/.test(value)
}

async function onSubmit() {
  if (form.newPassword !== form.confirmPassword) {
    showToast('两次输入的新密码不一致')
    return
  }
  submitting.value = true
  try {
    await changePassword({
      oldPassword: form.oldPassword,
      verifyInfo: form.verifyInfo,
      newPassword: form.newPassword,
      confirmPassword: form.confirmPassword
    })
    showSuccessToast('密码修改成功，请重新登录')
    auth.logout()
    router.replace('/login')
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.chgpwd-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.form-actions {
  margin: 24px 16px 0;
}

.form-tip {
  margin: 12px 20px;
  font-size: 12px;
  color: #969799;
  line-height: 1.7;
}
</style>
