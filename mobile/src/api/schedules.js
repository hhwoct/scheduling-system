import request from './request'
import { API_ROUTES } from '../constants/api'

// 排班计划列表（分页，可带 status 过滤）：返回 PagedResult { page, pageSize, total, items }
export function getPlans(params = {}) {
  return request.get(API_ROUTES.SCHEDULES.BASE, { params }).then((res) => res.data)
}

// 周视图：weekStart 'YYYY-MM-DD'（缺省后端取计划开始日）
export function getWeekView(planId, weekStart) {
  return request
    .get(API_ROUTES.SCHEDULES.WEEK_VIEW(planId), { params: weekStart ? { weekStart } : {} })
    .then((res) => res.data)
}

// 月视图：返回计划全范围内的员工×日数据
export function getMonthView(planId) {
  return request.get(API_ROUTES.SCHEDULES.MONTH_VIEW(planId)).then((res) => res.data)
}

// 日明细：workDate 'YYYY-MM-DD'，返回 { rows, demand }
export function getDailyView(planId, workDate) {
  return request
    .get(API_ROUTES.SCHEDULES.DAILY_VIEW(planId), { params: { workDate } })
    .then((res) => res.data)
}

// 排班问题列表（缺口/违规）：发布前展示统计
export function getIssues(planId) {
  return request.get(API_ROUTES.SCHEDULES.ISSUES(planId)).then((res) => res.data)
}

// 发布排班：存在 ERROR 严重违规时需 force=true（二次确认强制发布）
export function publishPlan(planId, force) {
  return request
    .post(API_ROUTES.SCHEDULES.PUBLISH(planId), null, { params: { force: !!force } })
    .then((res) => res.data)
}

// 退回草稿（取消发布）：员工端班表失效并收到通知
export function unpublishPlan(planId) {
  return request.post(API_ROUTES.SCHEDULES.UNPUBLISH(planId)).then((res) => res.data)
}
