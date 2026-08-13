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

      <el-pagination
        style="margin-top: 16px; justify-content: center"
        layout="total, prev, pager, next"
        :total="total"
        :page-size="pageSize"
        :current-page="page"
        @current-change="onPageChange"
      />
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getNotifications, getUnreadCount, markAsRead, markAllAsRead } from '../api/notifications'

const route = useRoute()
// 仅员工端路由使用 localStorage 兜底；管理端/notifications 不使用预览员工过滤
const employeeNo = computed(() => {
  if (route.query.employeeNo) return route.query.employeeNo
  if (route.path.startsWith('/employee/')) {
    return localStorage.getItem('shift_preview_employee_no') || ''
  }
  return ''
})

const list = ref([])
const unreadCount = ref(0)
const loading = ref(false)
// P3-36: 分页
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

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
    const params = { page: page.value, pageSize: pageSize.value }
    if (employeeNo.value) params.employeeNo = employeeNo.value
    const [notifs, countData] = await Promise.all([
      getNotifications(params),
      getUnreadCount(employeeNo.value || undefined)
    ])
    list.value = notifs?.items || notifs || []
    total.value = notifs?.total || 0
    unreadCount.value = countData?.count ?? 0
  } catch (e) {
    ElMessage.error('加载通知失败')
  } finally {
    loading.value = false
  }
}

function onPageChange(newPage) {
  page.value = newPage
  loadData()
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