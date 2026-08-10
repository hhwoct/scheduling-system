import request from '../utils/request'

export function getWorkstations(params = {}) {
  return request.get('/workstations', { params }).then(res => res.data)
}

export function createWorkstation(data) {
  return request.post('/workstations', data).then(res => res.data)
}

export function updateWorkstation(id, data) {
  return request.put(`/workstations/${id}`, data).then(res => res.data)
}
