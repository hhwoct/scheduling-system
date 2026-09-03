import request from './request'
import { API_ROUTES } from '../constants/api'

// 我的请假列表
export function getMyLeaves() {
  return request.get(API_ROUTES.LEAVE.MINE).then((res) => res.data)
}

// 提交请假：{ leaveType: PERSONAL|SICK|ANNUAL|OTHER, startDate, endDate, reason }
export function submitLeave(data) {
  return request.post(API_ROUTES.LEAVE.BASE, data).then((res) => res.data)
}

// 提前返岗：缩短已批准请假的结束日期（returnDate 必须晚于开始、早于原结束）
export function earlyReturnLeave(id, returnDate) {
  return request.put(API_ROUTES.LEAVE.EARLY_RETURN(id), { returnDate }).then((res) => res.data)
}
