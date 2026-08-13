import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

export function getEmployees(params) {
  return request.get(API_ROUTES.EMPLOYEES.BASE, { params }).then(res => res.data)
}

export function getEmployee(id) {
  return request.get(API_ROUTES.EMPLOYEES.DETAIL(id)).then(res => res.data)
}

export function createEmployee(data) {
  return request.post(API_ROUTES.EMPLOYEES.BASE, data).then(res => res.data)
}

export function updateEmployee(id, data) {
  return request.put(API_ROUTES.EMPLOYEES.DETAIL(id), data).then(res => res.data)
}

export function deactivateEmployee(id) {
  return request.delete(API_ROUTES.EMPLOYEES.DETAIL(id)).then(res => res.data)
}

export function getEmployeeSkills(employeeId) {
  return request.get(API_ROUTES.EMPLOYEES.SKILLS(employeeId)).then(res => res.data)
}

export function saveEmployeeSkills(employeeId, data) {
  return request.put(API_ROUTES.EMPLOYEES.SKILLS(employeeId), data).then(res => res.data)
}