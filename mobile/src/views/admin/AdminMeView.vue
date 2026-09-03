<template>
  <div class="admin-me-page">
    <van-nav-bar title="我的" />

    <div class="me-card">
      <div class="me-name">{{ auth.user?.nickname || auth.username }}</div>
      <div class="me-sub">系统管理员 · {{ auth.username }}</div>
    </div>

    <van-cell-group inset>
      <van-cell title="修改密码" is-link icon="shield-o" @click="router.push('/admin/me/password')" />
    </van-cell-group>

    <div class="logout-wrap">
      <van-button block round type="danger" plain @click="handleLogout">退出登录</van-button>
    </div>

    <div class="version-tip">排班系统 · 手机端 v0.1.0</div>
  </div>
</template>

<script setup>
import { useRouter } from 'vue-router'
import { showToast, showConfirmDialog } from 'vant'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const auth = useAuthStore()

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
.admin-me-page {
  min-height: 100vh;
  background: #f2f2f7;
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
