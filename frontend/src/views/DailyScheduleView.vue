<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>日排班明细（工作站 × 时间矩阵）</span>
          <div>
            <el-input v-model="planId" placeholder="排班计划 ID" style="width: 160px; margin-right: 8px" />
            <el-date-picker v-model="workDate" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-right: 8px" />
            <el-button type="primary" :loading="loading" @click="loadData">查询</el-button>
          </div>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" :closable="false" style="margin-bottom: 12px" />

      <!-- 整周期问题：无具体日期（工时超限等） -->
      <el-alert
        v-if="summaryIssues.length"
        type="warning"
        :closable="false"
        style="margin-bottom: 12px"
      >
        <template #title>整周期问题（{{ summaryIssues.length }} 条）</template>
        <div v-for="(si, idx) in summaryIssues.slice(0, 5)" :key="idx" style="font-size: 12px; margin-top: 2px">
          {{ si.description }}
        </div>
        <span v-if="summaryIssues.length > 5" style="font-size: 12px">… 等 {{ summaryIssues.length }} 条</span>
      </el-alert>

      <div v-loading="loading" class="matrix-wrap">
        <div class="matrix">
          <!-- 表头：时间轴（13:00 为原点，跨天到次日 05:30） -->
          <div class="m-row m-header">
            <div class="m-ws-col">工作站</div>
            <div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display">
              <span v-if="isHour(slot)">{{ slot.display }}</span>
            </div>
          </div>

          <!-- 每个工作站一行 -->
          <div v-for="ws in workstations" :key="ws" class="m-row">
            <div class="m-ws-col">{{ ws }}</div>
            <div v-for="slot in slots" :key="slot.key" class="m-slot-col" :class="cellClass(ws, slot)">
              <div v-if="slotIssues(ws, slot).length" class="gap-flag">缺</div>
              <div v-for="emp in cellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-break': inBreak(emp, slot) }">
                <div class="emp-name">
                  {{ emp.employeeName }}
                  <span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span>
                </div>
                <div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div>
                <div v-else class="emp-shift break-info">{{ emp.breakCoverEmployeeName ? `顶班 ${emp.breakCoverEmployeeName}` : '' }}</div>
                <div class="emp-pos">{{ emp.employeePosition || '--' }}</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div style="margin-top: 12px; display: flex; gap: 16px; align-items: center; flex-wrap: wrap">
        <span style="font-size: 12px; color: #909399">色块从上到下：名字 / 班次 / 职位；时间轴从当日 13:00 到次日 05:30（+1 表示次日，覆盖 06:00 下班的班次）</span>
        <el-tag size="small" type="warning">次日</el-tag>
        <el-tag size="small" type="danger">缺</el-tag>
        <span style="font-size: 12px; color: #909399">该工作站该时段存在岗位缺口</span>
      </div>
    </el-card>
  </div>
</template>

<script setup>
function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
import { onMounted, ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { getDailyView, getScheduleIssues } from '../api/schedules'

const route = useRoute()
const planId = ref(route.query.planId || '')
const workDate = ref(route.query.workDate || '')
const loading = ref(false)
const rows = ref([])
const issues = ref([])
const errorMsg = ref('')

// 时间轴：13:00 为原点，每 30 分钟一段，共 34 段（13:00~次日 05:30，覆盖 06:00 下班的班次；后端时段为左闭右开区间）
const SLOT_COUNT = 34
const slots = computed(() => {
  const list = []
  const startMin = 13 * 60
  for (let i = 0; i < SLOT_COUNT; i++) {
    const totalMin = startMin + i * 30
    const isNextDay = totalMin >= 24 * 60
    const hour = Math.floor((totalMin % (24 * 60)) / 60)
    const minute = totalMin % 60
    const key = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
    list.push({ key, display: isNextDay ? key + '+1' : key, isNextDay })
  }
  return list
})

// 日期 + N 天
function addDays(dateStr, days) {
  if (!dateStr) return ''
  const d = new Date(dateStr + 'T00:00:00')
  d.setDate(d.getDate() + days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 预计算：员工行按 工作站|时段 建立索引，避免模板内反复 O(n) 扫描
const cellUserMap = computed(() => {
  const map = new Map()
  for (const r of rows.value) {
    if (!r.workstationName) continue
    const hm = String(r.timeSlot).substring(0, 5)
    const key = r.workstationName + '|' + hm
    if (!map.has(key)) map.set(key, [])
    map.get(key).push(r)
  }
  return map
})

// 预计算：岗位缺口按 工作站|日期|时段 建立索引
const gapIssueMap = computed(() => {
  const map = new Map()
  for (const i of issues.value) {
    if (i.issueType !== 'STAFFING_GAP') continue
    const date = String(i.workDate || '').substring(0, 10)
    const hm = i.timeSlot ? String(i.timeSlot).substring(0, 5) : ''
    const key = i.workstationName + '|' + date + '|' + hm
    if (!map.has(key)) map.set(key, [])
    map.get(key).push(i)
  }
  return map
})

// 工作站列表：来自员工行，并补充当日/次日存在岗位缺口的站点（完全无人排班的站点也能显示）
const workstations = computed(() => {
  const set = new Set(rows.value.map(r => r.workstationName).filter(Boolean))
  if (workDate.value) {
    const nextDay = addDays(workDate.value, 1)
    for (const i of issues.value) {
      if (i.issueType !== 'STAFFING_GAP' || !i.workstationName) continue
      const d = String(i.workDate || '').substring(0, 10)
      if (d === workDate.value || d === nextDay) set.add(i.workstationName)
    }
  }
  return Array.from(set)
})

// 整周期问题（无具体日期，如工时超限）
const summaryIssues = computed(() => issues.value.filter(i => i.workDate == null))

function isHour(slot) { return slot.key.endsWith(':00') }

function cellUsers(ws, slot) {
  return cellUserMap.value.get(ws + '|' + slot.key) || []
}

// 该员工在该时段是否处于班中休息（含跨午夜回绕）
function inBreak(row, slot) {
  // 兼职员工不显示休息标记（排班界面直接留空）
  if (Number(row.isParttime) === 1) return false
  if (!row.breakStartTime || !row.breakEndTime) return false
  const s = String(row.breakStartTime).substring(0, 5)
  const e = String(row.breakEndTime).substring(0, 5)
  const t = slot.key
  if (e > s) return t >= s && t < e
  return t >= s || t < e
}

function breakTip(row) {
  const s = String(row.breakStartTime).substring(0, 5)
  const e = String(row.breakEndTime).substring(0, 5)
  return row.breakCoverEmployeeName
    ? `休息 ${s}-${e}，由 ${row.breakCoverEmployeeName} 顶班`
    : `休息 ${s}-${e}`
}

// 该格子对应的岗位缺口（按 日期+工作站+时段 匹配；次日格日期 +1）
function slotIssues(ws, slot) {
  const date = slot.isNextDay ? addDays(workDate.value, 1) : workDate.value
  return gapIssueMap.value.get(ws + '|' + date + '|' + slot.key) || []
}

function cellClass(ws, slot) {
  const users = cellUsers(ws, slot)
  const hasGap = slotIssues(ws, slot).length > 0
  let cls = ''
  if (users.length > 0) cls += 'has-employee'
  if (slot.isNextDay) cls += ' next-day'
  if (hasGap) cls += ' has-gap'
  return cls
}

async function loadData() {
  if (!planId.value) {
    errorMsg.value = '请输入排班计划 ID'
    return
  }
  if (!workDate.value) {
    workDate.value = route.query.workDate || getToday()
  }
  loading.value = true
  errorMsg.value = ''
  try {
    const [r, iss] = await Promise.all([
      getDailyView(planId.value, workDate.value),
      getScheduleIssues(planId.value)
    ])
    rows.value = r || []
    issues.value = iss || []
  } catch (e) {
    rows.value = []
    issues.value = []
    errorMsg.value = '查询失败：' + (e.message || '网络错误')
  } finally {
    loading.value = false
  }
}

onMounted(loadData)
</script>

<style scoped>
.matrix-wrap { overflow: auto; max-height: 560px; position: relative; }
.matrix { min-width: 100%; border: 1px solid #ebeef5; border-radius: 4px; }
.m-row { display: flex; border-bottom: 1px solid #ebeef5; }
.m-row:last-child { border-bottom: none; }
.m-header { background: #f5f7fa; font-weight: 600; position: sticky; top: 0; z-index: 4; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; position: sticky; left: 0; background: #fff; z-index: 3; display: flex; align-items: center; }
.m-header .m-ws-col { background: #f5f7fa; z-index: 5; }
.m-slot-col { width: 72px; min-height: 56px; flex-shrink: 0; padding: 2px 3px; border-right: 1px solid #f5f7fa; font-size: 11px; text-align: center; transition: background 0.2s; position: relative; }
.m-slot-col:last-child { border-right: none; }
.has-employee { background: #ecf5ff; }
.next-day { background: #fdf6ec; }
.has-gap { box-shadow: inset 0 0 0 2px #f56c6c; }
.gap-flag { position: absolute; top: 1px; right: 1px; background: #f56c6c; color: #fff; font-size: 10px; border-radius: 2px; padding: 0 3px; line-height: 14px; }
.emp-chip { background: #409eff; color: #fff; border-radius: 3px; padding: 2px 4px; margin-bottom: 2px; font-size: 11px; }
.emp-chip.is-break { background: #909399; }
.emp-chip.is-break .break-info { color: #ffe6a7; }
.break-flag { display: inline-block; background: #e6a23c; color: #fff; border-radius: 2px; padding: 0 3px; margin-left: 4px; font-size: 10px; line-height: 14px; }
.emp-chip .emp-name { font-weight: 600; }
.emp-chip .emp-shift { opacity: 0.95; }
.emp-chip .emp-pos { opacity: 0.8; font-size: 10px; }
.next-day .emp-chip { background: #e6a23c; }
</style>
