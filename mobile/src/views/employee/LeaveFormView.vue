<template>
  <div class="leave-form-page">
    <van-nav-bar title="提交请假" left-arrow @click-left="router.back()" />

    <van-form @submit="onSubmit">
      <van-cell-group inset>
        <van-field name="leaveType" label="请假类型">
          <template #input>
            <van-radio-group v-model="form.leaveType" direction="horizontal">
              <van-radio v-for="t in leaveTypes" :key="t.value" :name="t.value">{{ t.label }}</van-radio>
            </van-radio-group>
          </template>
        </van-field>

        <van-field
          v-model="dateText"
          readonly
          is-link
          name="range"
          label="请假日期"
          placeholder="选择起止日期"
          :rules="[{ required: true, message: '请选择请假日期' }]"
          @click="showCalendar = true"
        />

        <van-field
          v-model="form.reason"
          name="reason"
          label="事由"
          type="textarea"
          rows="2"
          autosize
          maxlength="200"
          show-word-limit
          placeholder="选填，说明请假原因"
        />
      </van-cell-group>

      <div class="form-actions">
        <van-button round block type="primary" native-type="submit" :loading="submitting">
          提交申请
        </van-button>
      </div>

      <div class="form-tip">
        提示：请假日期与已发布排班的上班安排冲突、或与已有请假重叠时将被拒绝；开始日期不能早于今天，单次请假不超过 30 天。
      </div>
    </van-form>

    <van-calendar
      v-model:show="showCalendar"
      type="range"
      :min-date="minDate"
      :max-date="maxDate"
      allow-same-day
      title="选择请假日期"
      @confirm="onRangeConfirm"
    />
  </div>
</template>

<script setup>
import { reactive, ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { showToast, showSuccessToast } from 'vant'
import { submitLeave } from '../../api/leave'

const route = useRoute()
const router = useRouter()

const leaveTypes = [
  { label: '事假', value: 'PERSONAL' },
  { label: '病假', value: 'SICK' },
  { label: '年假', value: 'ANNUAL' },
  { label: '其他', value: 'OTHER' }
]

const form = reactive({ leaveType: 'PERSONAL', startDate: '', endDate: '', reason: '' })
const submitting = ref(false)
const showCalendar = ref(false)

const minDate = dayjs().toDate()
const maxDate = dayjs().add(30, 'day').toDate()

const dateText = computed(() =>
  form.startDate && form.endDate ? form.startDate + ' ~ ' + form.endDate : ''
)

function onRangeConfirm(values) {
  const [start, end] = values
  form.startDate = dayjs(start).format('YYYY-MM-DD')
  form.endDate = dayjs(end).format('YYYY-MM-DD')
  showCalendar.value = false
}

async function onSubmit() {
  if (!form.startDate || !form.endDate) {
    showToast('请选择请假日期')
    return
  }
  submitting.value = true
  try {
    await submitLeave({
      leaveType: form.leaveType,
      startDate: form.startDate,
      endDate: form.endDate,
      reason: form.reason || ''
    })
    showSuccessToast('请假申请已提交')
    router.replace(route.meta.backPath || '/employee/leave')
  } catch (e) {
    // 错误已由 request 拦截器统一提示
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.leave-form-page {
  min-height: 100vh;
  background: #f7f8fa;
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
</style>
