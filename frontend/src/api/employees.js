import request from '../utils/request'

export function getEmployees(params) {
  return request.get('/employees', { params }).then(res => res.data)
}

export function getEmployee(id) {
  return request.get(`/employees/${id}`).then(res => res.data)
}

export function createEmployee(data) {
  return request.post('/employees', data).then(res => res.data)
}

export function updateEmployee(id, data) {
  return request.put(`/employees/${id}`, data).then(res => res.data)
}

export function deactivateEmployee(id) {
  return request.delete(`/employees/${id}`).then(res => res.data)
}

export function getEmployeeSkills(employeeId) {
  return request.get(`/employees/${employeeId}/skills`).then(res => res.data)
}

export function saveEmployeeSkills(employeeId, data) {
  return request.put(`/employees/${employeeId}/skills`, data).then(res => res.data)
}
