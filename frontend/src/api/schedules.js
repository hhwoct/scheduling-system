import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

export function generateSchedule(data) {
  return request.post(API_ROUTES.SCHEDULES.GENERATE, data).then(res => res.data)
}

export function getSchedules(params) {
  return request.get(API_ROUTES.SCHEDULES.BASE, { params }).then(res => res.data)
}

export function getMonthView(planId) {
  return request.get(API_ROUTES.SCHEDULES.MONTH_VIEW(planId)).then(res => res.data)
}

export function getWeekView(planId, weekStart) {
  return request.get(API_ROUTES.SCHEDULES.WEEK_VIEW(planId), { params: { weekStart } }).then(res => res.data)
}

export function getDailyView(planId, workDate) {
  return request.get(API_ROUTES.SCHEDULES.DAILY_VIEW(planId), { params: { workDate } }).then(res => res.data)
}

export function getScheduleSummary(planId) {
  return request.get(API_ROUTES.SCHEDULES.SUMMARY(planId)).then(res => res.data)
}

// 拖动移动工作段（时间平移 + 换工作站）；data 需含 planId
export function moveScheduleSegment(data) {
  if (!data || !data.planId) {
    return Promise.reject(new Error('缺少排班计划 ID，请先在列表中选择计划'))
  }
  return request.put(`/schedules/${data.planId}/move-segment`, data).then(res => res.data)
}

export function adjustSchedule(planId, data) {
  return request.put(API_ROUTES.SCHEDULES.ADJUST(planId), data).then(res => res.data)
}

export function setDayStatus(planId, data) {
  return request.put(API_ROUTES.SCHEDULES.DAY_STATUS(planId), data).then(res => res.data)
}

export function setSlotStatus(planId, data) {
  return request.put(API_ROUTES.SCHEDULES.SLOT_STATUS(planId), data).then(res => res.data)
}

export function getSwapPlans() {
  return request.get('/employee/swap-plans').then(res => res.data)
}

export function getScheduleRationality(planId) {
  return request.get(API_ROUTES.SCHEDULES.RATIONALITY(planId)).then(res => res.data)
}

export function getScheduleIssues(planId) {
  return request.get(API_ROUTES.SCHEDULES.ISSUES(planId)).then(res => res.data)
}

export function deleteSchedule(planId) {
  return request.delete(API_ROUTES.SCHEDULES.DETAIL(planId)).then(res => res.data)
}

export function publishSchedule(planId, force = false) {
  return request.post(API_ROUTES.SCHEDULES.PUBLISH(planId), null, { params: { force } }).then(res => res.data)
}

// 发布前调整摘要（P0 交互）：对比生成快照返回改休/换班统计
export function getAdjustmentSummary(planId) {
  return request.get(`/schedules/${planId}/adjustment-summary`).then(res => res.data)
}

// 调整明细（店长修改全程记录）
export function getScheduleAdjustments(planId, page = 1, pageSize = 50) {
  return request.get(`/schedules/${planId}/adjustments`, { params: { page, pageSize } }).then(res => res.data)
}

// 需求联动建议（店长反复手动补人的时段）
export function getDemandInsights(planId) {
  return request.get(`/schedules/${planId}/demand-insights`).then(res => res.data)
}

export { getSchedules as fetchSchedules }