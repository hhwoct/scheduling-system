import request from './request'
import { API_ROUTES } from '../constants/api'

// 员工列表（分页，可按 name/employeeNo/department/status 筛选）
export function getEmployees(params = {}) {
  return request.get(API_ROUTES.EMPLOYEES.BASE, { params }).then((res) => res.data)
}

// 员工详情
export function getEmployeeDetail(id) {
  return request.get(API_ROUTES.EMPLOYEES.DETAIL(id)).then((res) => res.data)
}

// 新增员工
export function createEmployee(data) {
  return request.post(API_ROUTES.EMPLOYEES.BASE, data).then((res) => res.data)
}

// 编辑员工
export function updateEmployee(id, data) {
  return request.put(API_ROUTES.EMPLOYEES.DETAIL(id), data).then((res) => res.data)
}

// 停用员工
export function deactivateEmployee(id) {
  return request.delete(API_ROUTES.EMPLOYEES.DETAIL(id)).then((res) => res.data)
}
