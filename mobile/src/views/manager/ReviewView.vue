<template>
  <div class="review-page">
    <van-nav-bar title="审批" />

    <!-- 状态筛选 -->
    <div class="filter-chips">
      <span
        v-for="f in filters"
        :key="f.value"
        class="chip"
        :class="{ 'chip-active': currentFilter === f.value }"
        @click="currentFilter = f.value"
      >{{ f.label }}</span>
    </div>

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-loading v-if="loading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <template v-else>
        <div class="review-card" v-for="item in filteredItems" :key="item.kind + '-' + item.id" @click="openDetail(item)">
          <div class="rc-head">
            <span class="rc-kind" :class="item.kind === 'leave' ? 'kind-leave' : 'kind-swap'">
              {{ item.kind === 'leave' ? '请假' : '换班' }}
            </span>
            <span class="rc-name">{{ item.title }}</span>
            <span class="rc-status" :class="'st-' + item.status">
              <i class="st-dot"></i>{{ statusName(item.status) }}
            </span>
          </div>
          <div class="rc-line">{{ item.subtitle }}</div>
          <div class="rc-line rc-reason" v-if="item.reason">事由：{{ item.reason }}</div>
        </div>

        <van-empty v-if="!filteredItems.length" :description="emptyText" image-size="80" />
      </template>
    </van-pull-refresh>

    <!-- 审批弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>
            <span class="rc-kind" :class="current.kind === 'leave' ? 'kind-leave' : 'kind-swap'">
              {{ current.kind === 'leave' ? '请假' : '换班' }}
            </span>
            {{ current.kind === 'leave' ? '请假审批' : '换班审批' }}
          </span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>

        <van-cell-group inset>
          <template v-if="current.kind === 'leave'">
            <van-cell title="员工" :value="(current.raw.employee?.name || '--') + '（' + (current.raw.employee?.employeeNo || '--') + '）'" />
            <van-cell title="类型" :value="typeName(current.raw.leaveType)" />
            <van-cell title="期间" :value="current.raw.startDate + ' ~ ' + current.raw.endDate" />
          </template>
          <template v-else>
            <van-cell title="申请人" :value="(current.raw.requester?.name || '--') + '（' + (current.raw.requester?.employeeNo || '--') + '）'" />
            <van-cell title="换班对象" :value="(current.raw.target?.name || '--') + '（' + (current.raw.target?.employeeNo || '--') + '）'" />
            <van-cell title="换班日期" :value="current.raw.swapDate" />
          </template>
          <van-cell title="事由" :value="current.reason || '--'" />
          <van-cell title="状态">
            <template #value>
              <span class="rc-status" :class="'st-' + current.status">
                <i class="st-dot"></i>{{ statusName(current.status) }}
              </span>
            </template>
          </van-cell>
          <van-cell v-if="current.reviewRemark" title="审批意见" :value="current.reviewRemark" />
        </van-cell-group>

        <template v-if="current.status === 'PENDING'">
          <van-cell-group inset class="remark-group">
            <van-field
              v-model.trim="remark"
              type="textarea"
              rows="2"
              autosize
              maxlength="200"
              show-word-limit
              placeholder="审批意见（选填，驳回时建议填写原因）"
            />
          </van-cell-group>
          <div class="action-row">
            <van-button round block type="danger" plain :loading="acting" @click="act(false)">驳回</van-button>
            <van-button round block type="success" :loading="acting" @click="act(true)">同意</van-button>
          </div>
        </template>
        <div class="detail-note" v-else>该申请已处理，不可重复审批</div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { showSuccessToast, showConfirmDialog } from 'vant'
import { getLeaveReviews, reviewLeave, getSwapReviews, reviewSwap } from '../../api/reviews'
import { useReviewStore } from '../../stores/reviews'
import { useAuthStore } from '../../stores/auth'

const reviewStore = useReviewStore()
const auth = useAuthStore()

const leaveItems = ref([])
const swapItems = ref([])
const loading = ref(false)
const refreshing = ref(false)
const currentFilter = ref('PENDING')

const filters = [
  { label: '待审批', value: 'PENDING' },
  { label: '已审批', value: 'REVIEWED' },
  { label: '全部', value: 'ALL' }
]

function typeName(t) {
  return { PERSONAL: '事假', SICK: '病假', ANNUAL: '年假', OTHER: '其他' }[t] || t
}

function statusName(s) {
  if (s === 'PENDING') return '待审批'
  if (s === 'APPROVED') return '已通过'
  if (s === 'REJECTED') return '已驳回'
  return s
}

// 请假 + 换班合并为统一列表（按提交时间倒序）
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
      title: (x.employee?.name || '--') + '（' + (x.employee?.employeeNo || '--') + '）',
      subtitle: typeName(x.leaveType) + ' · ' + x.startDate + ' ~ ' + x.endDate
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
    title:
      (x.requester?.name || '--') + '（' + (x.requester?.employeeNo || '--') + '）' +
      ' ⇄ ' + (x.target?.name || '--') + '（' + (x.target?.employeeNo || '--') + '）',
    subtitle: '换班日期 · ' + x.swapDate
  }
}

const mergedItems = computed(() => {
  const all = [
    ...leaveItems.value.map((x) => toItem('leave', x)),
    ...swapItems.value.map((x) => toItem('swap', x))
  ]
  // 审批对象是「店员」：排除店长自己提交的申请（店长自己的申请在「我的」里查看）
  const mine = auth.username || ''
  const others = all.filter((x) => {
    if (x.kind === 'leave') return x.raw.employee?.employeeNo !== mine
    return x.raw.requester?.employeeNo !== mine
  })
  others.sort((a, b) => String(b.createdAt || '').localeCompare(String(a.createdAt || '')))
  return others
})

const filteredItems = computed(() => {
  if (currentFilter.value === 'ALL') return mergedItems.value
  if (currentFilter.value === 'PENDING') return mergedItems.value.filter((x) => x.status === 'PENDING')
  // 已审批 = 已通过 + 已驳回
  return mergedItems.value.filter((x) => x.status === 'APPROVED' || x.status === 'REJECTED')
})

const emptyText = computed(() => {
  if (currentFilter.value === 'ALL') return '暂无审批申请'
  const f = filters.find((x) => x.value === currentFilter.value)
  return '暂无' + (f ? f.label : '') + '的申请'
})

async function load() {
  loading.value = true
  try {
    const [leave, swap] = await Promise.all([getLeaveReviews(), getSwapReviews()])
    leaveItems.value = leave || []
    swapItems.value = swap || []
    reviewStore.fetchCounts()
  } catch (e) {
    leaveItems.value = []
    swapItems.value = []
  } finally {
    loading.value = false
  }
}

async function reload() {
  await load()
  refreshing.value = false
}

const showDetail = ref(false)
const current = ref(null)
const remark = ref('')
const acting = ref(false)

function openDetail(item) {
  current.value = item
  remark.value = ''
  showDetail.value = true
}

async function act(approved) {
  if (!current.value) return
  const isLeave = current.value.kind === 'leave'
  const verb = approved ? '同意' : '驳回'
  try {
    await showConfirmDialog({
      title: verb + '确认',
      message: isLeave
        ? '确定' + verb + '该请假申请？'
        : '确定' + verb + '该换班申请？批准后班次将互换。'
    })
  } catch (e) {
    return
  }
  acting.value = true
  try {
    const payload = { approved, remark: remark.value || '' }
    if (isLeave) await reviewLeave(current.value.id, payload)
    else await reviewSwap(current.value.id, payload)
    showSuccessToast('已' + verb)
    showDetail.value = false
    await load()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    acting.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.review-page {
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

.filter-chips {
  display: flex;
  gap: 8px;
  padding: 10px 16px;
}

.chip {
  font-size: 13px;
  padding: 4px 14px;
  border-radius: 8px;
  background: #fff;
  color: var(--cal-sub);
  border: 1px solid #ebedf0;
}

.chip-active {
  background: #eaf3ff;
  color: var(--cal-blue);
  border-color: var(--cal-blue);
  font-weight: 600;
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

.review-card {
  margin: 8px 16px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}

.rc-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.rc-kind {
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

.rc-name {
  flex: 1;
  min-width: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--cal-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rc-status {
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

.rc-line {
  margin-top: 6px;
  font-size: 13px;
  color: var(--cal-text);
}

.rc-reason {
  color: var(--cal-sub);
  font-size: 12px;
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

.detail-head .rc-kind {
  min-width: 28px;
  height: 28px;
  font-size: 11px;
  margin-right: 6px;
}

.remark-group {
  margin-top: 12px;
}

.action-row {
  display: flex;
  gap: 12px;
  margin: 20px 16px 0;
}

.detail-note {
  margin: 16px 20px;
  font-size: 13px;
  color: var(--cal-sub);
  text-align: center;
}
</style>
