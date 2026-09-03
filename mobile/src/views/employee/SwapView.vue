<template>
  <div class="swap-page">
    <van-nav-bar title="我的换班">
      <template #right>
        <van-icon name="plus" size="20" @click="goNew" />
      </template>
    </van-nav-bar>

    <van-tabs v-model:active="filter" sticky :offset-top="46">
      <van-tab v-for="f in filters" :key="f.value" :title="f.label" :name="f.value" />
    </van-tabs>

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-cell-group inset class="swap-list" v-if="filtered.length">
        <van-cell v-for="item in filtered" :key="item.id" is-link @click="openDetail(item)">
          <template #title>
            <span class="swap-title">{{ item.swapDate }}</span>
            <span class="swap-target">与 {{ targetName(item) }} 换班</span>
          </template>
          <template #label>
            <div class="swap-label">{{ item.reason || '无备注' }}</div>
          </template>
          <template #value>
            <van-tag :type="statusType(item.status)">{{ statusName(item.status) }}</van-tag>
          </template>
        </van-cell>
      </van-cell-group>
      <van-empty v-else-if="!loading" :description="emptyText" image-size="80" />
    </van-pull-refresh>

    <div class="submit-bar">
      <van-button block round type="primary" icon="exchange" @click="goNew">发起换班</van-button>
    </div>

    <!-- 详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>换班详情</span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <van-cell-group inset>
          <van-cell title="换班日期" :value="current.swapDate" />
          <van-cell title="换班对象" :value="targetName(current)" />
          <van-cell title="事由" :value="current.reason || '--'" />
          <van-cell title="状态">
            <template #value>
              <van-tag :type="statusType(current.status)">{{ statusName(current.status) }}</van-tag>
            </template>
          </van-cell>
          <van-cell v-if="current.reviewRemark" title="审批意见" :value="current.reviewRemark" />
          <van-cell title="提交时间" :value="fmtDateTime(current.createdAt)" />
        </van-cell-group>
        <div class="detail-tip">
          {{ statusTip(current.status) }}
        </div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { getMySwaps } from '../../api/swap'

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
  // 已审批 = 已同意 + 已驳回
  return list.value.filter((x) => x.status === 'APPROVED' || x.status === 'REJECTED')
})

const emptyText = computed(() => {
  if (filter.value === 'ALL') return '暂无换班申请'
  const f = filters.find((x) => x.value === filter.value)
  return '暂无' + (f ? f.label : '') + '的换班'
})

function statusName(s) {
  return { PENDING: '待审批', APPROVED: '已同意', REJECTED: '已驳回' }[s] || s
}

function statusType(s) {
  return s === 'APPROVED' ? 'success' : s === 'REJECTED' ? 'danger' : 'warning'
}

function targetName(item) {
  return item?.target?.name || (item?.target?.employeeNo ? '工号 ' + item.target.employeeNo : '--')
}

function fmtDateTime(t) {
  return t ? dayjs(t).format('YYYY-MM-DD HH:mm') : '--'
}

function statusTip(s) {
  if (s === 'APPROVED') return '换班已生效：对方的休息日与你互换，请以最新班表为准。'
  if (s === 'REJECTED') return '该申请已被店长驳回。'
  return '申请已提交，等待店长审批。'
}

async function load() {
  loading.value = true
  try {
    list.value = await getMySwaps()
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
  router.push('/employee/swap/new')
}

const showDetail = ref(false)
const current = ref(null)

function openDetail(item) {
  current.value = item
  showDetail.value = true
}

onMounted(load)
</script>

<style scoped>
.swap-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.swap-list {
  margin-top: 8px;
}

.swap-title {
  font-size: 15px;
  font-weight: 600;
  color: #323233;
  margin-right: 8px;
}

.swap-target {
  font-size: 12px;
  color: #969799;
}

.swap-label {
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

.detail-tip {
  margin: 12px 20px 0;
  font-size: 12px;
  color: #969799;
  line-height: 1.7;
}
</style>
