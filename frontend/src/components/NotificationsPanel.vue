<template>
  <el-popover
    placement="bottom-end"
    :width="380"
    trigger="click"
    popper-class="notif-pop"
    @show="loadList"
  >
    <template #reference>
      <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99">
        <el-icon class="notif-bell" :size="20"><Bell /></el-icon>
      </el-badge>
    </template>

    <div class="notif-panel">
      <div class="u-row-between u-mb-4">
        <strong>通知</strong>
        <el-button link type="primary" size="small" :disabled="unreadCount === 0" @click="handleReadAll">
          全部已读
        </el-button>
      </div>

      <el-empty
        v-if="!loading && list.length === 0"
        description="暂无通知"
        :image-size="60"
      />
      <div v-else v-loading="loading" class="notif-list">
        <div
          v-for="item in list"
          :key="item.id"
          class="notif-item"
          :class="{ unread: item.isRead === 0 }"
          @click="handleRead(item)"
        >
          <div class="u-row-between">
            <el-tag size="small" :type="item.isRead === 0 ? 'primary' : 'info'">{{ typeName(item.notificationType) }}</el-tag>
            <span class="notif-time">{{ fmtTime(item.createdAt) }}</span>
          </div>
          <div class="notif-title">{{ item.title }}</div>
          <div class="notif-content">{{ item.content }}</div>
        </div>
      </div>

      <div class="notif-footer">
        <el-button link type="primary" size="small" @click="goAll">查看全部</el-button>
      </div>
    </div>
  </el-popover>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Bell } from '@element-plus/icons-vue'
import { getNotifications, getUnreadCount, markAsRead, markAllAsRead } from '../api/notifications'

const props = defineProps({
  /** 通知列表目标路由,打开「查看全部」时跳转 */
  viewAllPath: { type: String, default: '/notifications' },
  /** 是否按员工工号过滤(员工端预览模式需要) */
  employeeNo: { type: String, default: '' }
})

const router = useRouter()
const loading = ref(false)
const list = ref([])
const unreadCount = ref(0)

function typeName(t) {
  return {
    SCHEDULE_PUBLISHED: '排班发布',
    SCHEDULE_UNPUBLISHED: '排班取消',
    SCHEDULE_CHANGED: '排班变更',
    LEAVE_APPROVED: '请假审批',
    LEAVE_REJECTED: '请假驳回',
    SWAP_APPROVED: '换班批准',
    SWAP_REJECTED: '换班驳回'
  }[t] || t
}

function fmtTime(t) {
  if (!t) return ''
  const d = new Date(t)
  if (Number.isNaN(d.getTime())) return ''
  const pad = n => String(n).padStart(2, '0')
  return `${d.getMonth() + 1}/${d.getDate()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function loadList() {
  loading.value = true
  try {
    const params = { page: 1, pageSize: 8 }
    if (props.employeeNo) params.employeeNo = props.employeeNo
    const [notifs, countData] = await Promise.all([
      getNotifications(params),
      getUnreadCount(props.employeeNo || undefined)
    ])
    const items = Array.isArray(notifs) ? notifs : (notifs?.items || [])
    list.value = items
    unreadCount.value = countData?.count ?? 0
  } catch (e) {
    /* 静默失败:铃铛降级为不可用,不打断页面 */
  } finally {
    loading.value = false
  }
}

async function handleRead(item) {
  if (item.isRead === 1) return
  try {
    await markAsRead(item.id)
    item.isRead = 1
    unreadCount.value = Math.max(0, unreadCount.value - 1)
    window.dispatchEvent(new CustomEvent('notifications-changed'))
  } catch (e) {
    /* 拦截器已提示 */
  }
}

async function handleReadAll() {
  try {
    await markAllAsRead()
    list.value.forEach(i => { i.isRead = 1 })
    unreadCount.value = 0
    window.dispatchEvent(new CustomEvent('notifications-changed'))
  } catch (e) {
    /* 拦截器已提示 */
  }
}

function goAll() {
  router.push(props.viewAllPath)
}

// 页面内标记已读后会派发该事件,同步未读数
function onChanged() {
  loadList()
}

onMounted(() => window.addEventListener('notifications-changed', onChanged))
onUnmounted(() => window.removeEventListener('notifications-changed', onChanged))
</script>

<style scoped>
.notif-bell {
  cursor: pointer;
  color: var(--el-text-color-regular);
}
.notif-bell:hover {
  color: var(--el-color-primary);
}
.notif-list {
  max-height: 320px;
  overflow-y: auto;
}
.notif-item {
  padding: var(--app-space-4);
  border-radius: var(--app-radius-sm);
  cursor: pointer;
}
.notif-item:hover {
  background: var(--el-fill-color-light);
}
.notif-item.unread {
  background: var(--el-color-primary-light-9);
}
.notif-title {
  margin-top: var(--app-space-2);
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.notif-content {
  margin-top: var(--app-space-1);
  font-size: var(--app-font-sm);
  color: var(--el-text-color-regular);
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.notif-time {
  font-size: var(--app-font-xs);
  color: var(--el-text-color-secondary);
}
.notif-footer {
  border-top: 1px solid var(--el-border-color-lighter);
  padding-top: var(--app-space-3);
  text-align: center;
}
</style>
