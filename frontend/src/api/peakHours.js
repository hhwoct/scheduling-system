import request from '../utils/request'

export function getPeakHours() {
  return request.get('/peak-restricted-hours').then(res => res.data)
}

export function createPeakHour(data) {
  return request.post('/peak-restricted-hours', data).then(res => res.data)
}

export function updatePeakHour(id, data) {
  return request.put(`/peak-restricted-hours/${id}`, data).then(res => res.data)
}

export function deletePeakHour(id) {
  return request.delete(`/peak-restricted-hours/${id}`).then(res => res.data)
}
