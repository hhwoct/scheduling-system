<template>
  <div class="emp-mgr-page">
    <van-nav-bar title="员工管理">
      <template #right>
        <van-icon name="plus" size="20" @click="goNew" />
      </template>
    </van-nav-bar>

    <!-- 筛选：部门 / 用工性质 / 门店（超管） -->
    <van-dropdown-menu>
      <van-dropdown-item v-model="deptFilter" :options="deptOptions" />
      <van-dropdown-item v-model="typeFilter" :options="typeOptions" />
      <van-dropdown-item v-if="isAdmin" v-model="storeFilter" :options="storeOptions" />
    </van-dropdown-menu>

    <van-search
      v-model="keyword"
      placeholder="搜索姓名 / 工号"
      shape="round"
    />

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-loading v-if="loading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <template v-else>
        <div class="emp-list">
          <div v-for="emp in filtered" :key="emp.id" class="emp-row" @click="openDetail(emp)">
            <div class="er-avatar">{{ emp.name.charAt(0) }}</div>
            <div class="er-main">
              <div class="er-name">
                {{ emp.name }}
                <span class="er-tag" v-if="emp.isParttime === 1">兼职</span>
              </div>
              <div class="er-sub">{{ emp.employeeNo }} · {{ emp.department || '未分部门' }}<template v-if="emp.primaryPosition"> · {{ emp.primaryPosition }}</template><template v-if="emp.storeName"> · {{ emp.storeName }}</template></div>
            </div>
            <van-icon name="arrow" class="er-arrow" />
          </div>
        </div>
        <van-empty v-if="!filtered.length" description="未找到匹配员工" image-size="80" />
      </template>
    </van-pull-refresh>

    <!-- 详情弹层 -->
    <van-popup v-model:show="showDetail" position="bottom" round>
      <div class="detail-pop" v-if="current">
        <div class="detail-head">
          <span>{{ current.name }}</span>
          <van-icon name="cross" @click="showDetail = false" />
        </div>
        <van-cell-group inset>
          <van-cell title="工号" :value="current.employeeNo" />
          <van-cell title="姓名" :value="current.name" />
          <van-cell title="部门" :value="current.department || '--'" />
          <van-cell title="岗位" :value="current.primaryPosition || '--'" />
          <van-cell title="手机号" :value="current.phone || '--'" />
          <van-cell title="用工性质" :value="current.isParttime === 1 ? '兼职' : '全职'" />
          <van-cell v-if="current.hireDate" title="入职日期" :value="current.hireDate" />
          <van-cell v-if="current.maxWeeklyHours" title="周工时上限" :value="current.maxWeeklyHours + ' 小时'" />
        </van-cell-group>
        <div class="action-row">
          <van-button round block plain type="danger" :loading="acting" @click="handleDeactivate">停用</van-button>
          <van-button round block type="primary" :loading="acting" @click="goEdit">编辑</van-button>
        </div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../../stores/auth'
import { showToast, showSuccessToast, showConfirmDialog } from 'vant'
import { getEmployees, deactivateEmployee } from '../../api/employees'
import { getStores } from '../../api/store'

const router = useRouter()
const auth = useAuthStore()
// 超管在自己的 /admin 体系下复用本页，新增/编辑跳转到 admin 路由
const base = computed(() => (auth.effectiveRole === 'admin' ? '/admin' : '/manager'))

const all = ref([])
const loading = ref(false)
const refreshing = ref(false)
const keyword = ref('')

// 筛选状态
const deptFilter = ref('ALL')
const typeFilter = ref('ALL')
const storeFilter = ref('ALL')

const isAdmin = computed(() => auth.effectiveRole === 'admin')
const stores = ref([])

const deptOptions = computed(() => [
  { text: '全部部门', value: 'ALL' },
  ...['管理', '行政', '工程', '保洁', '楼面', '厨房', '吧台', '兼职'].map((d) => ({ text: d, value: d }))
])

const typeOptions = [
  { text: '全部性质', value: 'ALL' },
  { text: '全职', value: 'FULLTIME' },
  { text: '兼职', value: 'PARTTIME' }
]

const storeOptions = computed(() => [
  { text: '全部门店', value: 'ALL' },
  ...stores.value.map((s) => ({ text: s.name, value: s.id }))
])

const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return all.value.filter((e) => {
    if (deptFilter.value !== 'ALL' && e.department !== deptFilter.value) return false
    if (typeFilter.value === 'FULLTIME' && e.isParttime === 1) return false
    if (typeFilter.value === 'PARTTIME' && e.isParttime !== 1) return false
    if (isAdmin.value && storeFilter.value !== 'ALL' && Number(e.storeId) !== Number(storeFilter.value)) return false
    if (kw) {
      const hit =
        String(e.name || '').toLowerCase().includes(kw) ||
        String(e.employeeNo || '').toLowerCase().includes(kw)
      if (!hit) return false
    }
    return true
  })
})

async function load() {
  loading.value = true
  try {
    const data = await getEmployees({ page: 1, pageSize: 100, status: 1 })
    const items = data?.items || []
    items.sort((a, b) => String(a.employeeNo).localeCompare(String(b.employeeNo)))
    all.value = items
  } catch (e) {
    all.value = []
  } finally {
    loading.value = false
  }
}

async function reload() {
  await load()
  refreshing.value = false
}

function goNew() {
  router.push(base.value + '/employees/new')
}

const showDetail = ref(false)
const current = ref(null)
const acting = ref(false)

function openDetail(emp) {
  current.value = emp
  showDetail.value = true
}

function goEdit() {
  if (!current.value) return
  showDetail.value = false
  router.push(base.value + '/employees/edit?id=' + current.value.id)
}

async function handleDeactivate() {
  if (!current.value) return
  const emp = current.value
  try {
    await showConfirmDialog({
      title: '停用员工',
      message: '停用后「' + emp.name + '（' + emp.employeeNo + '）」将无法登录、不再参与排班。确认停用？',
      confirmButtonText: '停用'
    })
  } catch (e) {
    return
  }
  acting.value = true
  try {
    await deactivateEmployee(emp.id)
    showSuccessToast('已停用')
    showDetail.value = false
    await load()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    acting.value = false
  }
}

onMounted(async () => {
  await load()
  if (isAdmin.value) {
    try {
      stores.value = (await getStores()) || []
    } catch (e) {
      stores.value = []
    }
  }
})
</script>

<style scoped>
.emp-mgr-page {
  min-height: 100vh;
  background: #f2f2f7;
  --cal-text: #1c1c1e;
  --cal-sub: #8e8e93;
  --cal-line: #e5e5ea;
  --cal-red: #ff3b30;
  --cal-blue: #007aff;
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

.emp-list {
  background: #fff;
  border-top: 1px solid var(--cal-line);
  border-bottom: 1px solid var(--cal-line);
}

.emp-row {
  display: flex;
  align-items: center;
  padding: 11px 16px;
  border-bottom: 1px solid var(--cal-line);
}

.emp-row:last-child {
  border-bottom: none;
}

.er-avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: #eaf3ff;
  color: var(--cal-blue);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 17px;
  font-weight: 600;
  flex-shrink: 0;
}

.er-main {
  flex: 1;
  min-width: 0;
  margin-left: 12px;
}

.er-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--cal-text);
}

.er-tag {
  display: inline-block;
  margin-left: 6px;
  font-size: 10px;
  color: #fff;
  background: var(--cal-orange, #ff9500);
  border-radius: 999px;
  padding: 1px 8px;
  vertical-align: 1px;
}

.er-sub {
  margin-top: 2px;
  font-size: 12px;
  color: var(--cal-sub);
}

.er-arrow {
  color: #c7c7cc;
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

.action-row {
  display: flex;
  gap: 12px;
  margin: 20px 16px 0;
}
</style>
