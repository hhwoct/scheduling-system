<template>
  <div class="profile-page">
    <van-nav-bar title="我的" />

    <!-- 个人信息卡片 -->
    <div class="user-card">
      <div class="avatar">{{ avatarChar }}</div>
      <div class="user-info">
        <div class="user-name">{{ employee?.name || auth.user?.nickname || auth.username }}</div>
        <div class="user-sub">
          {{ employee ? employee.employeeNo + ' · ' + (employee.department || '未分部门') : auth.username }}
        </div>
        <div class="user-store" v-if="storeName">{{ storeName }}</div>
      </div>
    </div>

    <van-cell-group inset>
      <van-cell title="工号" :value="employee?.employeeNo || auth.username" />
      <van-cell title="姓名" :value="employee?.name || auth.user?.nickname || '--'" />
      <van-cell title="部门" :value="employee?.department || '--'" />
      <van-cell title="门店" :value="storeName || '--'" />
    </van-cell-group>

    <van-cell-group inset class="pwd-entry">
      <van-cell title="修改密码" is-link icon="shield-o" @click="goChangePassword" />
    </van-cell-group>

    <div class="logout-wrap">
      <van-button block round type="danger" plain @click="handleLogout">退出登录</van-button>
    </div>

    <div class="version-tip">排班系统 · 手机端 v0.1.0</div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { showToast, showConfirmDialog } from 'vant'
import { useAuthStore } from '../../stores/auth'
import { getMySchedule } from '../../api/employee'
import { getCurrentStore } from '../../api/store'

const router = useRouter()
const auth = useAuthStore()

const employee = ref(null)
const storeName = ref('')

const avatarChar = computed(() => {
  const name = employee.value?.name || auth.user?.nickname || auth.username || '?'
  return name.charAt(0)
})

onMounted(async () => {
  try {
    const data = await getMySchedule()
    employee.value = data?.employee || null
  } catch (e) {
    // 无员工档案（如纯管理员账号）时静默
  }
  try {
    const store = await getCurrentStore()
    storeName.value = store?.name || ''
  } catch (e) {
    // 门店名获取失败不影响本页
  }
})

function goChangePassword() {
  router.push('/employee/profile/password')
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
.profile-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.user-card {
  display: flex;
  align-items: center;
  margin: 12px 16px;
  padding: 20px 16px;
  border-radius: 12px;
  background: linear-gradient(135deg, #1989fa, #0570db);
  color: #fff;
}

.avatar {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.25);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
  font-weight: 600;
  margin-right: 14px;
  flex-shrink: 0;
}

.user-info {
  flex: 1;
  min-width: 0;
}

.user-name {
  font-size: 19px;
  font-weight: 600;
}

.user-sub {
  margin-top: 4px;
  font-size: 13px;
  opacity: 0.9;
}

.user-store {
  margin-top: 2px;
  font-size: 12px;
  opacity: 0.75;
}

.pwd-entry {
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
