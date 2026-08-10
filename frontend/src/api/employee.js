import request from '../utils/request'

// 员工我的班表
export function getMySchedule(month) {
  return request.get('/employee/my-schedule', { params: { month } }).then(res => res.data)
}
