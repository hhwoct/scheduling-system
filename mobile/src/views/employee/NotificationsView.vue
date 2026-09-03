<template>
  <div class="notif-page">
    <van-nav-bar title="通知">
      <template #right>
        <span class="read-all" @click="handleReadAll">全部已读</span>
      </template>
    </van-nav-bar>

    <van-pull-refresh v-model="refreshing" @refresh="onRefresh">
      <van-list
        v-model:loading="listLoading"
        :finished="finished"
        finished-text="没有更多了"
        @load="onLoad"
      >
        <van-cell v-for="item in items" :key="item.id" is-link @click="openDetail(item)">
          <template #title>
            <span :class="{ 'notif-title-unread': item.isRead === 0 }">
              <i v-if="item.isRead === 0" class="unread-dot"></i>{{ decodeHtml(item.title) }}
            </span>
          </template>
          <template #label>
            <div class="notif-label">
              <div class="notif-content">{{ decodeHtml(item.content) }}</div>
              <div class="notif-time">{{ fmtTime(item.createdAt) }}</div>
            </div>
          </template>
        </van-cell>
      </van-list>
    </van-pull-refresh>

    <van-empty v-if="finished && !items.length" description="暂无通知" image-size="80" />

    <!-- 详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>{{ current.title }}</span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <div class="detail-content">{{ decodeHtml(current.content) }}</div>
        <div class="detail-time">{{ fmtTime(current.createdAt) }}</div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { showToast } from 'vant'
import {
  getNotifications,
  markNotificationRead,
  markAllNotificationsRead
} from '../../api/notifications'
import { useNotificationStore } from '../../stores/notifications'

const notifStore = useNotificationStore()

// 后端内容做了 HtmlEncode（如 &#183;），展示前解码
function decodeHtml(s) {
  if (!s) return ''
  const el = document.createElement('textarea')
  el.innerHTML = s
  return el.value
}

const items = ref([])
const page = ref(1)
const pageSize = 20
const total = ref(0)
const listLoading = ref(false)
const finished = ref(false)
const refreshing = ref(false)

function fmtTime(t) {
  if (!t) return '--'
  const d = dayjs(t)
  const now = dayjs()
  if (d.isSame(now, 'day')) return '今天 ' + d.format('HH:mm')
  if (d.isSame(now.subtract(1, 'day'), 'day')) return '昨天 ' + d.format('HH:mm')
  if (d.isSame(now, 'year')) return d.format('MM-DD HH:mm')
  return d.format('YYYY-MM-DD HH:mm')
}

async function loadPage(p) {
  try {
    const data = await getNotifications(p, pageSize)
    const list = data?.items || []
    total.value = Number(data?.total) || 0
    if (p === 1) items.value = list
    else items.value = items.value.concat(list)
    finished.value = items.value.length >= total.value
  } catch (e) {
    finished.value = true
  }
}

async function onLoad() {
  if (refreshing.value) return
  await loadPage(page.value)
  page.value += 1
  listLoading.value = false
}

async function onRefresh() {
  page.value = 1
  await loadPage(1)
  notifStore.fetchUnread()
  refreshing.value = false
}

const showDetail = ref(false)
const current = ref(null)

async function openDetail(item) {
  current.value = item
  showDetail.value = true
  if (item.isRead === 0) {
    item.isRead = 1
    try {
      await markNotificationRead(item.id)
      notifStore.fetchUnread()
    } catch (e) {
      // 网络失败时回滚展示状态，下次点击重试
      item.isRead = 0
    }
  }
}

async function handleReadAll() {
  try {
    await markAllNotificationsRead()
    items.value.forEach((x) => (x.isRead = 1))
    showToast('已全部标记为已读')
    notifStore.fetchUnread()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  }
}

onMounted(() => {
  notifStore.fetchUnread()
})
</script>

<style scoped>
.notif-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.read-all {
  font-size: 14px;
  color: #1989fa;
}

.notif-title-unread {
  font-weight: 600;
  color: #323233;
}

.unread-dot {
  display: inline-block;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #ee0a24;
  margin-right: 6px;
  vertical-align: middle;
}

.notif-label {
  margin-top: 2px;
}

.notif-content {
  font-size: 12px;
  color: #969799;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.notif-time {
  margin-top: 2px;
  font-size: 11px;
  color: #c8c9cc;
}

.detail-pop {
  padding: 12px 20px calc(20px + env(safe-area-inset-bottom));
}

.detail-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 12px;
  font-size: 16px;
  font-weight: 600;
  color: #323233;
}

.detail-content {
  font-size: 14px;
  color: #646566;
  line-height: 1.7;
}

.detail-time {
  margin-top: 12px;
  font-size: 12px;
  color: #c8c9cc;
}
</style>
