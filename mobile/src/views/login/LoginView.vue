<template>
  <div class="login-page">
    <div class="brand">
      <div class="brand-title">排班系统</div>
      <div class="brand-sub">手机端 · 随时随地看班表</div>
    </div>

    <van-form @submit="onSubmit">
      <van-cell-group inset>
        <van-field
          v-model.trim="form.username"
          name="username"
          label="工号"
          placeholder="请输入工号/用户名"
          :rules="[{ required: true, message: '请输入工号' }]"
          clearable
          autocomplete="username"
        />
        <van-field
          v-model="form.password"
          type="password"
          name="password"
          label="密码"
          placeholder="请输入密码"
          :rules="[{ required: true, message: '请输入密码' }]"
          autocomplete="current-password"
        />
      </van-cell-group>

      <div class="login-actions">
        <van-button
          round
          block
          type="primary"
          native-type="submit"
          :loading="loading"
          :disabled="cooldown > 0"
        >
          {{ cooldown > 0 ? `登录失败，${cooldown}s 后可重试` : '登录' }}
        </van-button>
      </div>

      <div class="login-links">
        <router-link to="/forgot-password">忘记密码？</router-link>
      </div>
    </van-form>
  </div>
</template>

<script setup>
import { reactive, ref, onUnmounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { showSuccessToast } from 'vant'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const form = reactive({ username: '', password: '' })
const loading = ref(false)

// 登录失败冷却：与 PC 端一致，防止连发触发后端失败锁定
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

onUnmounted(() => {
  if (cooldownTimer) clearInterval(cooldownTimer)
})

function homeForRole(effectiveRole) {
  if (effectiveRole === 'employee') return '/employee'
  if (effectiveRole === 'manager') return '/manager'
  if (effectiveRole === 'admin') return '/admin'
  return '/employee'
}

async function onSubmit() {
  if (loading.value || cooldown.value > 0) return
  loading.value = true
  try {
    await auth.login(form.username, form.password)
    showSuccessToast('登录成功')
    // 仅当 redirect 指向当前角色可访问区域时才跟随，否则回角色首页
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : ''
    const effective = auth.effectiveRole
    const home = homeForRole(effective)
    const allowed =
      (effective === 'employee' && redirect.startsWith('/employee')) ||
      (effective === 'manager' && redirect.startsWith('/manager'))
    router.replace(allowed ? redirect : home)
  } catch (e) {
    // 错误提示已由 request 拦截器统一弹出，这里只负责冷却
    startCooldown(5)
  } finally {
    loading.value = false
  }
}
</script>
