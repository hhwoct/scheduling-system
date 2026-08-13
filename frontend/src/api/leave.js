import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

// 员工提交请假
export function submitLeave(data) {
  return request.post(API_ROUTES.LEAVE.BASE, data).then(res => res.data)
}

// 员工我的请假（管理员可用 employeeNo 预览）
export function getMyLeaves(employeeNo) {
  return request.get(API_ROUTES.LEAVE.MINE, { params: { employeeNo } }).then(res => res.data)
}

// 管理员：请假审批列表
export function getLeaveReviewList(status) {
  return request.get(API_ROUTES.LEAVE.REVIEW, { params: { status } }).then(res => res.data)
}

// 管理员：审批（通过/驳回）
export function reviewLeave(id, data) {
  return request.put(API_ROUTES.LEAVE.REVIEW_DETAIL(id), data).then(res => res.data)
}

// 员工：提前返岗（缩短已批准请假）
export function earlyReturnLeave(id, returnDate) {
  return request.put(API_ROUTES.LEAVE.EARLY_RETURN(id), { returnDate }).then(res => res.data)
}