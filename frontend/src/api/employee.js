import request from '../utils/request'

// 员工我的班表（管理员可通过 employeeNo 预览）
export function getMySchedule(month, employeeNo) {
  return request.get('/employee/my-schedule', { params: { month, employeeNo } }).then(res => res.data)
}