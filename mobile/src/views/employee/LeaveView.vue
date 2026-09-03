<template>
  <div class="leave-page">
    <van-nav-bar title="我的请假">
      <template #right>
        <van-icon name="plus" size="20" @click="goNew" />
      </template>
    </van-nav-bar>

    <van-tabs v-model:active="filter" sticky :offset-top="46">
      <van-tab v-for="f in filters" :key="f.value" :title="f.label" :name="f.value" />
    </van-tabs>

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-cell-group inset class="leave-list" v-if="filtered.length">
        <van-cell v-for="item in filtered" :key="item.id" is-link @click="openDetail(item)">
          <template #title>
            <span class="leave-title">{{ typeName(item.leaveType) }}</span>
            <span class="leave-dates">{{ item.startDate }} ~ {{ item.endDate }}</span>
          </template>
          <template #label>
            <div class="leave-label">{{ item.reason || '无备注' }}</div>
          </template>
          <template #value>
            <van-tag :type="statusType(item.status)">{{ statusName(item.status) }}</van-tag>
          </template>
        </van-cell>
      </van-cell-group>
      <van-empty v-else-if="!loading" :description="emptyText" image-size="80" />
    </van-pull-refresh>

    <div class="submit-bar">
      <van-button block round type="primary" icon="plus" @click="goNew">提交请假</van-button>
    </div>

    <!-- 详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>请假详情</span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <van-cell-group inset>
          <van-cell title="类型" :value="typeName(current.leaveType)" />
          <van-cell title="开始日期" :value="current.startDate" />
          <van-cell title="结束日期" :value="current.endDate" />
          <van-cell title="事由" :value="current.reason || '--'" />
          <van-cell title="状态">
            <template #value>
              <van-tag :type="statusType(current.status)">{{ statusName(current.status) }}</van-tag>
            </template>
          </van-cell>
          <van-cell v-if="current.reviewRemark" title="审批意见" :value="current.reviewRemark" />
          <van-cell title="提交时间" :value="fmtDateTime(current.createdAt)" />
        </van-cell-group>
        <div class="detail-actions" v-if="canEarlyReturn(current)">
          <van-button block round type="primary" :loading="earlySubmitting" @click="openEarlyCalendar">
            提前返岗
          </van-button>
        </div>
      </div>
    </van-popup>

    <!-- 提前返岗选日期 -->
    <van-calendar
      v-model:show="showEarlyCalendar"
      :min-date="earlyMin"
      :max-date="earlyMax"
      title="选择返岗日期"
      @confirm="confirmEarly"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { showToast, showSuccessToast, showConfirmDialog } from 'vant'
import { getMyLeaves, earlyReturnLeave } from '../../api/leave'

const router = useRouter()

const list = ref([])
const loading = ref(false)
const refreshing = ref(false)
const filter = ref('ALL')

const filters = [
  { label: '全部', value: 'ALL' },
  { label: '待审批', value: 'PENDING' },
  { label: '已审批', value: 'REVIEWED' }
]

const filtered = computed(() => {
  if (filter.value === 'ALL') return list.value
  if (filter.value === 'PENDING') return list.value.filter((x) => x.status === 'PENDING')
  // 已审批 = 已批准 + 已驳回
  return list.value.filter((x) => x.status === 'APPROVED' || x.status === 'REJECTED')
})

const emptyText = computed(() => {
  if (filter.value === 'ALL') return '暂无请假记录'
  const f = filters.find((x) => x.value === filter.value)
  return '暂无' + (f ? f.label : '') + '的请假'
})

function typeName(t) {
  return { PERSONAL: '事假', SICK: '病假', ANNUAL: '年假', OTHER: '其他' }[t] || t
}

function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已批准', REJECTED: '已驳回' }[s] || s
}

function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

function fmtDateTime(t) {
  return t ? dayjs(t).format('YYYY-MM-DD HH:mm') : '--'
}

async function load() {
  loading.value = true
  try {
    list.value = await getMyLeaves()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    loading.value = false
  }
}

async function reload() {
  await load()
  refreshing.value = false
}

function goNew() {
  router.push('/employee/leave/new')
}

const showDetail = ref(false)
const current = ref(null)

function openDetail(item) {
  current.value = item
  showDetail.value = true
}

function canEarlyReturn(item) {
  return item.status === 'APPROVED' && item.startDate && item.endDate && item.startDate < item.endDate
}

const showEarlyCalendar = ref(false)
const earlySubmitting = ref(false)

const earlyMin = computed(() => {
  if (!current.value) return new Date()
  return dayjs(current.value.startDate).add(1, 'day').toDate()
})

const earlyMax = computed(() => {
  if (!current.value) return new Date()
  return dayjs(current.value.endDate).subtract(1, 'day').toDate()
})

function openEarlyCalendar() {
  if (!current.value) return
  showEarlyCalendar.value = true
}

async function confirmEarly(date) {
  const returnDate = dayjs(date).format('YYYY-MM-DD')
  const label = current.value.startDate + ' 至 ' + returnDate
  try {
    await showConfirmDialog({
      title: '提前返岗',
      message: '请假将缩短为 ' + label + '，确定吗？'
    })
  } catch (e) {
    showEarlyCalendar.value = false
    return
  }
  showEarlyCalendar.value = false
  earlySubmitting.value = true
  try {
    await earlyReturnLeave(current.value.id, returnDate)
    showSuccessToast('已更新为提前返岗')
    showDetail.value = false
    await load()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    earlySubmitting.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.leave-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.leave-list {
  margin-top: 8px;
}

.leave-title {
  font-size: 15px;
  font-weight: 600;
  color: #323233;
  margin-right: 8px;
}

.leave-dates {
  font-size: 12px;
  color: #969799;
}

.leave-label {
  margin-top: 2px;
  font-size: 12px;
  color: #969799;
}

.submit-bar {
  margin: 16px 16px 24px;
}

.detail-pop {
  padding: 12px 0 calc(20px + env(safe-area-inset-bottom));
}

.detail-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 20px 12px;
  font-size: 16px;
  font-weight: 600;
  color: #323233;
}

.detail-actions {
  margin: 20px 16px 0;
}
</style>
