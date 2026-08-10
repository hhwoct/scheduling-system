import request from '../utils/request'

export function getRules() {
  return request.get('/rules').then(res => res.data)
}

export function updateRule(id, data) {
  return request.put(`/rules/${id}`, data).then(res => res.data)
}
