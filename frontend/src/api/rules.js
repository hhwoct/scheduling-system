import request from '../utils/request'

export function getRules() {
  return request.get('/rules').then(res => res.data)
}

export function updateRule(id, data) {
  return request.put(`/rules/${id}`, data).then(res => res.data)
}

// 店长删除本店覆盖行(恢复全局默认)
export function deleteRule(id) {
  return request.delete(`/rules/${id}`).then(res => res.data)
}
