<template>
  <div class="rules-page">
    <van-nav-bar title="规则配置" />

    <div class="rules-tip">全局默认规则，对所有门店生效（店长可覆盖个别规则）。</div>

    <van-pull-refresh v-model="refreshing" @refresh="reload">
      <van-loading v-if="loading" class="page-loading" size="24" vertical>加载中…</van-loading>

      <template v-else>
        <div class="rule-card" v-for="r in rules" :key="r.id" @click="openEdit(r)">
          <div class="rc-head">
            <span class="rc-name">{{ r.ruleName }}</span>
            <span class="rc-status" :class="Number(r.status) === 1 ? 'on' : 'off'">
              <i class="st-dot"></i>{{ Number(r.status) === 1 ? '启用' : '停用' }}
            </span>
          </div>
          <div class="rc-value">当前值：{{ displayValue(r) }}</div>
          <div class="rc-remark" v-if="r.remark">{{ r.remark }}</div>
        </div>
        <van-empty v-if="!rules.length" description="暂无规则" image-size="80" />
      </template>
    </van-pull-refresh>

    <!-- 编辑弹层 -->
    <van-popup v-model:show="showEdit" position="bottom" round>
      <div class="edit-pop" v-if="current">
        <div class="edit-head">
          <span>{{ current.ruleName }}</span>
          <van-icon name="cross" @click="showEdit = false" />
        </div>
        <van-cell-group inset>
          <van-field
            v-if="current.valueType === 'json'"
            v-model="editValue"
            type="textarea"
            rows="3"
            autosize
            label="规则值"
            placeholder="JSON 格式"
          />
          <van-field
            v-else-if="current.valueType === 'number'"
            v-model="editValue"
            type="number"
            label="规则值"
            placeholder="请输入数值"
          />
          <van-field v-else v-model="editValue" label="规则值" placeholder="请输入内容" />
          <van-cell title="启用该规则">
            <template #right-icon>
              <van-switch v-model="editStatus" :active-value="1" :inactive-value="0" size="22" />
            </template>
          </van-cell>
          <van-cell v-if="current.remark" title="说明" :label="current.remark" />
        </van-cell-group>
        <div class="edit-actions">
          <van-button round block type="primary" :loading="saving" @click="save">保存</van-button>
        </div>
      </div>
    </van-popup>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { showToast, showSuccessToast } from 'vant'
import { getRules, updateRule } from '../../api/rules'

const rules = ref([])
const loading = ref(false)
const refreshing = ref(false)

function displayValue(r) {
  const v = String(r.ruleValue ?? '')
  if (v.length > 60) return v.slice(0, 60) + '…'
  return v
}

async function load() {
  loading.value = true
  try {
    rules.value = (await getRules()) || []
  } catch (e) {
    rules.value = []
  } finally {
    loading.value = false
  }
}

async function reload() {
  await load()
  refreshing.value = false
}

const showEdit = ref(false)
const current = ref(null)
const editValue = ref('')
const editStatus = ref(1)
const saving = ref(false)

function openEdit(r) {
  current.value = r
  editValue.value = String(r.ruleValue ?? '')
  editStatus.value = Number(r.status) === 1 ? 1 : 0
  showEdit.value = true
}

async function save() {
  if (!current.value) return
  if (!String(editValue.value).trim()) {
    showToast('规则值不能为空')
    return
  }
  saving.value = true
  try {
    await updateRule(current.value.id, {
      ruleValue: String(editValue.value).trim(),
      status: editStatus.value,
      version: current.value.version ?? null
    })
    showSuccessToast('已保存')
    showEdit.value = false
    await load()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.rules-page {
  min-height: 100vh;
  background: #f2f2f7;
  --cal-green: #34c759;
  --cal-red: #ff3b30;
  --cal-text: #1c1c1e;
  --cal-sub: #8e8e93;
  --cal-line: #e5e5ea;
}

.rules-tip {
  margin: 10px 20px;
  font-size: 12px;
  color: var(--cal-sub);
}

.page-loading {
  margin: 24px auto;
  display: block;
  text-align: center;
}

.rule-card {
  margin: 8px 16px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}

.rc-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.rc-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--cal-text);
}

.rc-status {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
}

.rc-status .st-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  display: inline-block;
}

.rc-status.on {
  color: var(--cal-green);
}

.rc-status.on .st-dot {
  background: var(--cal-green);
}

.rc-status.off {
  color: var(--cal-sub);
}

.rc-status.off .st-dot {
  background: var(--cal-sub);
}

.rc-value {
  margin-top: 6px;
  font-size: 14px;
  color: var(--cal-text);
}

.rc-remark {
  margin-top: 4px;
  font-size: 12px;
  color: var(--cal-sub);
}

.edit-pop {
  padding: 12px 0 calc(20px + env(safe-area-inset-bottom));
}

.edit-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 20px 12px;
  font-size: 16px;
  font-weight: 700;
  color: var(--cal-text);
}

.edit-actions {
  margin: 20px 16px 0;
}
</style>
