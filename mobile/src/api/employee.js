import request from './request'
import { API_ROUTES } from '../constants/api'

// 获取我的班表；month 为 'YYYY-MM'，不传时后端默认近 30 天
export function getMySchedule(month) {
  return request
    .get(API_ROUTES.EMPLOYEE.MY_SCHEDULE, { params: month ? { month } : {} })
    .then((res) => res.data)
}
