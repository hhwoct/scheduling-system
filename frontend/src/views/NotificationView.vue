<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>通知消息</span>
          <el-button size="small" :disabled="unreadCount === 0" @click="handleReadAll">全部标为已读</el-button>
        </div>
      </template>

      <el-empty v-if="!loading && list.length === 0" description="暂无通知" />

      <el-timeline v-else v-loading="loading">
        <el-timeline-item
          v-for="item in list"
          :key="item.id"
          :timestamp="fmtTime(item.createdAt)"
          placement="top"
          :type="item.isRead === 0 ? 'primary' : 'info'"
        >
          <el-card :class="{ unread: item.isRead === 0 }" shadow="hover">
            <div style="display: flex; align-items: center; justify-content: space-between">
              <div>
                <el-tag size="small" style="margin-right: 8px">{{ typeName(item.notificationType) }}</el-tag>
                <strong>{{ item.title }}</strong>
              </div>
              <el-button v-if="item.isRead === 0" link type="primary" size="small" @click="handleRead(item.id)">标为已读</el-button>
            </div>
            <div style="margin-top: 8px; color: #606266">{{ item.content }}</div>
          </el-card>
        </el-timeline-item>
      </el-timeline>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getNotifications, getUnreadCount, markAsRead, markAllAsRead } from '../api/notifications'

const list = ref([])
const unreadCount = ref(0)
const loading = ref(false)

function typeName(t) {
  return {
    SCHEDULE_PUBLISHED: '排班发布',
    LEAVE_APPROVED: '请假审批',
    LEAVE_REJECTED: '请假驳回',
    SWAP_APPROVED: '换班批准',
    SWAP_REJECTED: '换班驳回'
  }[t] || t
}

function fmtTime(t) {
  if (!t) return ''
  return String(t).replace('T', ' ').substring(0, 19)
}

async function loadData() {
  loading.value = true
  try {
    const [notifs, countData] = await Promise.all([getNotifications(), getUnreadCount()])
    list.value = notifs || []
    unreadCount.value = countData?.count ?? 0
  } catch (e) {
    ElMessage.error('加载通知失败')
  } finally {
    loading.value = false
  }
}

async function handleRead(id) {
  await markAsRead(id)
  ElMessage.success('已读')
  loadData()
}

async function handleReadAll() {
  await markAllAsRead()
  ElMessage.success('全部已读')
  loadData()
}

onMounted(loadData)
</script>

<style scoped>
.unread {
  border-left: 3px solid #409eff;
}
</style>