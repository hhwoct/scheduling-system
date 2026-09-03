<template>
  <div class="home-page">
    <van-nav-bar title="提示" />

    <div class="notice-box">
      <van-icon name="desktop-o" size="48" color="#c8c9cc" />
      <div class="notice-title">请使用电脑端</div>
      <div class="notice-sub">
        系统管理员的排班规则配置、员工管理等功能操作复杂，请在电脑浏览器中打开管理端使用。
      </div>
      <van-button block round type="primary" class="notice-btn" @click="handleLogout">
        退出登录
      </van-button>
    </div>
  </div>
</template>

<script setup>
import { useRouter } from 'vue-router'
import { showToast } from 'vant'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const auth = useAuthStore()

function handleLogout() {
  auth.logout()
  showToast('已退出登录')
  router.replace('/login')
}
</script>

<style scoped>
.home-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.notice-box {
  margin-top: 64px;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 0 32px;
  text-align: center;
}

.notice-title {
  margin-top: 16px;
  font-size: 18px;
  font-weight: 600;
  color: #323233;
}

.notice-sub {
  margin-top: 8px;
  font-size: 14px;
  color: #969799;
  line-height: 1.7;
}

.notice-btn {
  margin-top: 32px;
}
</style>
