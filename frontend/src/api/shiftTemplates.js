import request from '../utils/request'

export function getShiftTemplates() {
  return request.get('/shift-templates').then(res => res.data)
}

export function updateShiftTemplate(id, data) {
  return request.put(`/shift-templates/${id}`, data).then(res => res.data)
}
