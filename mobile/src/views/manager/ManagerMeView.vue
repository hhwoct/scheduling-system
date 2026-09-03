<template>
  <div class="manager-me-page">
    <van-nav-bar title="我的" />

    <div class="me-card">
      <div class="me-name">{{ auth.user?.nickname || auth.username }}</div>
      <div class="me-sub">{{ roleText }}</div>
    </div>

    <van-cell-group inset>
      <van-cell title="我的请假与换班" is-link icon="exchange" @click="go('/manager/my-leave-swap')" />
    </van-cell-group>

    <van-cell-group inset class="sec-group">
      <van-cell title="修改密码" is-link icon="shield-o" @click="go('/manager/me/password')" />
    </van-cell-group>

    <div class="logout-wrap">
      <van-button block round type="danger" plain @click="handleLogout">退出登录</van-button>
    </div>

    <div class="version-tip">排班系统 · 手机端 v0.1.0</div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { showToast, showConfirmDialog } from 'vant'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const auth = useAuthStore()

const roleText = computed(() => '店长 · ' + (auth.username || ''))

function go(path) {
  router.push(path)
}

async function handleLogout() {
  try {
    await showConfirmDialog({ title: '退出登录', message: '确认退出当前账号？' })
  } catch (e) {
    return
  }
  auth.logout()
  showToast('已退出登录')
  router.replace('/login')
}
</script>

<style scoped>
.manager-me-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.me-card {
  margin: 12px 16px;
  padding: 20px 16px;
  border-radius: 12px;
  background: linear-gradient(135deg, #1989fa, #0570db);
  color: #fff;
}

.me-name {
  font-size: 20px;
  font-weight: 600;
}

.me-sub {
  margin-top: 4px;
  font-size: 13px;
  opacity: 0.85;
}

.sec-group {
  margin-top: 12px;
}

.logout-wrap {
  margin: 24px 16px;
}

.version-tip {
  text-align: center;
  font-size: 12px;
  color: #c8c9cc;
  margin-bottom: 24px;
}
</style>
