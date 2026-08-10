import request from '../utils/request'

export function generateSchedule(data) {
  return request.post('/schedules/generate', data).then(res => res.data)
}

export function getSchedules(params) {
  return request.get('/schedules', { params }).then(res => res.data)
}

export function getMonthView(planId) {
  return request.get(`/schedules/${planId}/month-view`).then(res => res.data)
}

export function getWeekView(planId, weekStart) {
  return request.get(`/schedules/${planId}/week-view`, { params: { weekStart } }).then(res => res.data)
}

export function getDailyView(planId, workDate) {
  return request.get(`/schedules/${planId}/daily-view`, { params: { workDate } }).then(res => res.data)
}

export function getScheduleSummary(planId) {
  return request.get(`/schedules/${planId}/summary`).then(res => res.data)
}

export function adjustSchedule(planId, data) {
  return request.put(`/schedules/${planId}/adjust`, data).then(res => res.data)
}

export function getScheduleIssues(planId) {
  return request.get(`/schedules/${planId}/issues`).then(res => res.data)
}

export function deleteSchedule(planId) {
  return request.delete(`/schedules/${planId}`).then(res => res.data)
}

export function publishSchedule(planId) {
  return request.post(`/schedules/${planId}/publish`).then(res => res.data)
}

export { getSchedules as fetchSchedules }
