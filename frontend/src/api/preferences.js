import request from '../utils/request'

// 偏好学习仪表盘（feature/schedule-pref-learning）
export function getPreferenceStats() {
  return request.get('/preferences/stats').then(res => res.data)
}

export function getPreferenceMatrix(dayType) {
  return request.get('/preferences/matrix', { params: { dayType } }).then(res => res.data)
}

export function getPreferenceTop(limit = 20) {
  return request.get('/preferences/top', { params: { limit } }).then(res => res.data)
}

export function getPreferenceTrends() {
  return request.get('/preferences/trends').then(res => res.data)
}

export function rebuildPreferences() {
  return request.post('/preferences/rebuild').then(res => res.data)
}
