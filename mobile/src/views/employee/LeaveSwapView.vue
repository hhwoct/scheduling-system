<template>
  <div class="ls-page">
    <van-nav-bar title="换班&请假" />

    <van-tabs v-model:active="filter" sticky :offset-top="46">
      <van-tab v-for="f in filters" :key="f.value" :title="f.label" :name="f.value" />
    </van-tabs>

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-loading v-if="loading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <template v-else>
        <div class="ls-card" v-for="item in filtered" :key="item.kind + '-' + item.id" @click="openDetail(item)">
          <div class="ls-head">
            <span class="ls-kind" :class="item.kind === 'leave' ? 'kind-leave' : 'kind-swap'">
              {{ item.kind === 'leave' ? '请假' : '换班' }}
            </span>
            <span class="ls-title">{{ item.title }}</span>
            <span class="ls-status" :class="'st-' + item.status">
              <i class="st-dot"></i>{{ statusName(item) }}
            </span>
          </div>
          <div class="ls-sub">{{ item.subtitle }}</div>
          <div class="ls-reason" v-if="item.reason">事由：{{ item.reason }}</div>
        </div>
        <van-empty v-if="!filtered.length" :description="emptyText" image-size="80" />
      </template>
    </van-pull-refresh>

    <div class="ls-actions">
      <van-button round block plain type="primary" icon="notes-o" @click="go(route.meta.createLeavePath || '/employee/leave/new')">发起请假</van-button>
      <van-button round block type="primary" icon="exchange" @click="go(route.meta.createSwapPath || '/employee/swap/new')">发起换班</van-button>
    </div>

    <!-- 详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>
            <span class="ls-kind" :class="current.kind === 'leave' ? 'kind-leave' : 'kind-swap'">
              {{ current.kind === 'leave' ? '请假' : '换班' }}
            </span>
            {{ current.kind === 'leave' ? '请假详情' : '换班详情' }}
          </span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <van-cell-group inset>
          <template v-if="current.kind === 'leave'">
            <van-cell title="类型" :value="typeName(current.raw.leaveType)" />
            <van-cell title="开始日期" :value="current.raw.startDate" />
            <van-cell title="结束日期" :value="current.raw.endDate" />
          </template>
          <template v-else>
            <van-cell title="换班日期" :value="current.raw.swapDate" />
            <van-cell title="换班对象" :value="targetName(current)" />
          </template>
          <van-cell title="事由" :value="current.reason || '--'" />
          <van-cell title="状态">
            <template #value>
              <span class="ls-status" :class="'st-' + current.status">
                <i class="st-dot"></i>{{ statusName(current) }}
              </span>
            </template>
          </van-cell>
          <van-cell v-if="current.reviewRemark" title="审批意见" :value="current.reviewRemark" />
          <van-cell title="提交时间" :value="fmtDateTime(current.createdAt)" />
        </van-cell-group>
        <div class="detail-note">{{ statusTip(current) }}</div>
        <div class="detail-action" v-if="canEarlyReturn(current)">
          <van-button round block type="primary" :loading="earlySubmitting" @click="openEarlyCalendar">
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
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { showToast, showSuccessToast, showConfirmDialog } from 'vant'
import { getMyLeaves, earlyReturnLeave } from '../../api/leave'
import { getMySwaps } from '../../api/swap'

const route = useRoute()
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
  return list.value.filter((x) => x.status === 'APPROVED' || x.status === 'REJECTED')
})

const emptyText = computed(() => {
  if (filter.value === 'ALL') return '暂无请假/换班记录'
  const f = filters.find((x) => x.value === filter.value)
  return '暂无' + (f ? f.label : '') + '的记录'
})

function typeName(t) {
  return { PERSONAL: '事假', SICK: '病假', ANNUAL: '年假', OTHER: '其他' }[t] || t
}

function statusName(item) {
  const s = item.status
  if (s === 'PENDING') return '待审批'
  if (s === 'APPROVED') return item.kind === 'leave' ? '已批准' : '已同意'
  if (s === 'REJECTED') return '已驳回'
  return s
}

function targetName(item) {
  if (item.kind !== 'swap') return '--'
  const t = item.raw?.target
  return t?.name || (t?.employeeNo ? '工号 ' + t.employeeNo : '--')
}

function statusTip(item) {
  if (item.status === 'PENDING') return '申请已提交，等待店长审批。'
  if (item.kind === 'swap') {
    return item.status === 'APPROVED'
      ? '换班已生效：对方的休息日与你互换，请以最新班表为准。'
      : '该申请已被店长驳回。'
  }
  return item.status === 'APPROVED' ? '请假已批准。' : '请假已被驳回。'
}

function fmtDateTime(t) {
  return t ? dayjs(t).format('YYYY-MM-DD HH:mm') : '--'
}

function toItem(kind, x) {
  if (kind === 'leave') {
    return {
      kind,
      id: x.id,
      status: x.status,
      reason: x.reason,
      reviewRemark: x.reviewRemark,
      createdAt: x.createdAt,
      raw: x,
      title: typeName(x.leaveType),
      subtitle: x.startDate + ' ~ ' + x.endDate
    }
  }
  return {
    kind,
    id: x.id,
    status: x.status,
    reason: x.reason,
    reviewRemark: x.reviewRemark,
    createdAt: x.createdAt,
    raw: x,
    title: '与 ' + targetName({ kind, raw: x }) + ' 换班',
    subtitle: '换班日期 · ' + x.swapDate
  }
}

async function load() {
  loading.value = true
  try {
    const [leaves, swaps] = await Promise.all([getMyLeaves(), getMySwaps()])
    const all = [
      ...(leaves || []).map((x) => toItem('leave', x)),
      ...(swaps || []).map((x) => toItem('swap', x))
    ]
    all.sort((a, b) => String(b.createdAt || '').localeCompare(String(a.createdAt || '')))
    list.value = all
  } catch (e) {
    list.value = []
  } finally {
    loading.value = false
  }
}

async function reload() {
  await load()
  refreshing.value = false
}

function go(path) {
  router.push(path)
}

const showDetail = ref(false)
const current = ref(null)

function openDetail(item) {
  current.value = item
  showDetail.value = true
}

function canEarlyReturn(item) {
  return (
    item.kind === 'leave' &&
    item.status === 'APPROVED' &&
    item.raw?.startDate &&
    item.raw?.endDate &&
    item.raw.startDate < item.raw.endDate
  )
}

const showEarlyCalendar = ref(false)
const earlySubmitting = ref(false)

const earlyMin = computed(() => {
  if (!current.value) return new Date()
  return dayjs(current.value.raw.startDate).add(1, 'day').toDate()
})

const earlyMax = computed(() => {
  if (!current.value) return new Date()
  return dayjs(current.value.raw.endDate).subtract(1, 'day').toDate()
})

function openEarlyCalendar() {
  showEarlyCalendar.value = true
}

async function confirmEarly(date) {
  const returnDate = dayjs(date).format('YYYY-MM-DD')
  const label = current.value.raw.startDate + ' 至 ' + returnDate
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
.ls-page {
  min-height: 100vh;
  background: #f2f2f7;
  --cal-red: #ff3b30;
  --cal-blue: #007aff;
  --cal-orange: #ff9500;
  --cal-green: #34c759;
  --cal-text: #1c1c1e;
  --cal-sub: #8e8e93;
  --cal-line: #e5e5ea;
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

.ls-card {
  margin: 8px 16px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}

.ls-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ls-kind {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  height: 32px;
  padding: 0 6px;
  border-radius: 50%;
  font-size: 12px;
  font-weight: 700;
  color: #fff;
  flex-shrink: 0;
}

.kind-leave {
  background: var(--cal-orange);
}

.kind-swap {
  background: var(--cal-blue);
}

.ls-title {
  flex: 1;
  min-width: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--cal-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ls-status {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  flex-shrink: 0;
}

.st-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  display: inline-block;
}

.st-PENDING {
  color: var(--cal-orange);
}

.st-PENDING .st-dot {
  background: var(--cal-orange);
}

.st-APPROVED {
  color: var(--cal-green);
}

.st-APPROVED .st-dot {
  background: var(--cal-green);
}

.st-REJECTED {
  color: var(--cal-red);
}

.st-REJECTED .st-dot {
  background: var(--cal-red);
}

.ls-sub {
  margin-top: 6px;
  font-size: 13px;
  color: var(--cal-text);
}

.ls-reason {
  margin-top: 2px;
  font-size: 12px;
  color: var(--cal-sub);
}

.ls-actions {
  display: flex;
  gap: 12px;
  margin: 16px;
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
  font-weight: 700;
  color: var(--cal-text);
}

.detail-head .ls-kind {
  min-width: 28px;
  height: 28px;
  font-size: 11px;
  margin-right: 6px;
}

.detail-note {
  margin: 12px 20px 0;
  font-size: 12px;
  color: var(--cal-sub);
}

.detail-action {
  margin: 16px 16px 0;
}
</style>
