<template>
  <div class="swap-form-page">
    <van-nav-bar title="发起换班" left-arrow @click-left="router.back()" />

    <van-form @submit="onSubmit">
      <van-cell-group inset>
        <van-field
          v-model="swapDate"
          readonly
          is-link
          name="date"
          label="换班日期"
          placeholder="选择你要换掉的上班日"
          :rules="[{ required: true, message: '请选择换班日期' }]"
          @click="showCalendar = true"
        />
        <van-field name="target" label="换班对象" readonly :rules="[{ required: true, message: '请选择换班同事' }]">
          <template #input>
            <span v-if="selectedTarget" class="target-picked">
              {{ selectedTarget.name }}（{{ selectedTarget.employeeNo }}）
            </span>
            <span v-else class="target-empty">选择日期后从当天休息的同事中选择</span>
          </template>
        </van-field>
      </van-cell-group>

      <!-- 候选同事列表 -->
      <div class="cand-section" v-if="swapDate">
        <div class="cand-head">
          <span>{{ swapDate }} 休息的同事<template v-if="swapDate < todayStr">（已过去）</template></span>
          <span class="cand-status" v-if="!candLoading">
            <template v-if="candidates.length">共 {{ candidates.length }} 人，选一位与你换班</template>
            <template v-else>暂无休息同事可选</template>
          </span>
        </div>
        <van-loading v-if="candLoading" class="cand-loading" size="20" />
        <van-radio-group v-model="targetId" class="cand-list">
          <van-cell-group inset>
            <van-cell
              v-for="c in candidates"
              :key="c.id"
              clickable
              :title="c.name"
              :label="c.employeeNo + ' · ' + (c.department || '--')"
              @click="targetId = c.id"
            >
              <template #right-icon>
                <van-radio :name="c.id" />
              </template>
            </van-cell>
          </van-cell-group>
        </van-radio-group>
      </div>

      <van-cell-group inset class="reason-group">
        <van-field
          v-model="form.reason"
          name="reason"
          label="事由"
          type="textarea"
          rows="2"
          autosize
          maxlength="200"
          show-word-limit
          placeholder="选填，说明换班原因"
        />
      </van-cell-group>

      <div class="form-actions">
        <van-button round block type="primary" native-type="submit" :loading="submitting">
          提交换班申请
        </van-button>
      </div>

      <div class="form-tip">
        换班规则：只能选你自己上班、对方休息的日期；提交后由店长审批，批准后你们的班次互换。
      </div>
    </van-form>

    <van-calendar
      v-model:show="showCalendar"
      :min-date="minDate"
      :max-date="maxDate"
      :formatter="formatter"
      title="选择换班日期"
      @confirm="onDateConfirm"
    />
  </div>
</template>

<script setup>
import { reactive, ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { showToast, showSuccessToast } from 'vant'
import { getSwapPlans, getSwapCandidates, createSwap } from '../../api/swap'
import { getMySchedule } from '../../api/employee'

const route = useRoute()
const router = useRouter()

const plans = ref([])
const myWork = new Set()
const myRest = new Set()

const form = reactive({ reason: '' })
const submitting = ref(false)
const showCalendar = ref(false)

const swapDate = ref('')
const candidates = ref([])
const candLoading = ref(false)
const targetId = ref(null)

const selectedTarget = computed(() => candidates.value.find((c) => c.id === targetId.value) || null)

const todayStr = dayjs().format('YYYY-MM-DD')

const minDate = computed(() => {
  if (!plans.value.length) return dayjs().toDate()
  const d = dayjs(plans.value.reduce((m, p) => (p.startDate < m ? p.startDate : m), plans.value[0].startDate))
  return d.isBefore(dayjs()) ? dayjs().toDate() : d.toDate()
})

const maxDate = computed(() => {
  if (!plans.value.length) return dayjs().add(30, 'day').toDate()
  return dayjs(plans.value.reduce((m, p) => (p.endDate > m ? p.endDate : m), plans.value[0].endDate)).toDate()
})

function formatter(day) {
  const date = dayjs(day.date).format('YYYY-MM-DD')
  const inRange = plans.value.some((p) => date >= p.startDate && date <= p.endDate)
  if (!inRange) {
    return { ...day, type: 'disabled' }
  }
  if (myWork.has(date)) return { ...day, topInfo: '班', className: 'cal-work' }
  if (myRest.has(date)) return { ...day, topInfo: '休', className: 'cal-rest' }
  return day
}

function planForDate(date) {
  return plans.value.find((p) => date >= p.startDate && date <= p.endDate) || null
}

async function onDateConfirm(date) {
  swapDate.value = dayjs(date).format('YYYY-MM-DD')
  showCalendar.value = false
  targetId.value = null
  candidates.value = []

  const plan = planForDate(swapDate.value)
  if (!plan) {
    showToast('该日期不在已发布排班范围内')
    return
  }
  candLoading.value = true
  try {
    candidates.value = await getSwapCandidates(plan.id, swapDate.value)
    if (!candidates.value.length) {
      showToast('当天没有休息的同事可选')
    }
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    candLoading.value = false
  }
}

async function onSubmit() {
  if (!swapDate.value) {
    showToast('请选择换班日期')
    return
  }
  if (!targetId.value) {
    showToast('请选择换班同事')
    return
  }
  const plan = planForDate(swapDate.value)
  if (!plan) {
    showToast('该日期不在已发布排班范围内')
    return
  }
  submitting.value = true
  try {
    await createSwap({
      planId: plan.id,
      swapDate: swapDate.value,
      targetEmployeeId: targetId.value,
      reason: form.reason || ''
    })
    showSuccessToast('换班申请已提交')
    router.replace(route.meta.backPath || '/employee/leave')
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  try {
    plans.value = await getSwapPlans()
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  }
  // 我的班表：用于日历上标注 班/休
  try {
    const data = await getMySchedule()
    ;(data?.plans || []).forEach((p) => {
      ;(p.days || []).forEach((d) => {
        const date = String(d.workDate)
        if (d.isRestDay === 1) myRest.add(date)
        else myWork.add(date)
      })
    })
  } catch (e) {
    // 忽略：仅影响日历标注
  }
})
</script>

<style scoped>
.swap-form-page {
  min-height: 100vh;
  background: #f7f8fa;
}

.target-picked {
  color: #323233;
}

.target-empty {
  color: #969799;
  font-size: 13px;
}

.cand-section {
  margin-top: 12px;
}

.cand-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 16px 8px;
  font-size: 13px;
  color: #646566;
}

.cand-status {
  color: #969799;
}

.cand-loading {
  margin: 8px 0;
  display: block;
  text-align: center;
}

.cand-list {
  display: block;
}

.reason-group {
  margin-top: 12px;
}

.form-actions {
  margin: 24px 16px 0;
}

.form-tip {
  margin: 12px 20px;
  font-size: 12px;
  color: #969799;
  line-height: 1.7;
}

:deep(.cal-work .van-calendar__top-info) {
  color: #1989fa;
}

:deep(.cal-rest .van-calendar__top-info) {
  color: #ee0a24;
}
</style>
