import request from './request'
import { API_ROUTES } from '../constants/api'

// 请假审批列表（status 可选：PENDING/APPROVED/REJECTED）
export function getLeaveReviews(status) {
  return request.get(API_ROUTES.REVIEWS.LEAVE_LIST, { params: status ? { status } : {} }).then((res) => res.data)
}

// 审批请假：{ approved, remark }
export function reviewLeave(id, data) {
  return request.put(API_ROUTES.REVIEWS.LEAVE_REVIEW(id), data).then((res) => res.data)
}

// 换班审批列表（status 可选）
export function getSwapReviews(status) {
  return request.get(API_ROUTES.REVIEWS.SWAP_LIST, { params: status ? { status } : {} }).then((res) => res.data)
}

// 审批换班：{ approved, remark }
export function reviewSwap(id, data) {
  return request.put(API_ROUTES.REVIEWS.SWAP_REVIEW(id), data).then((res) => res.data)
}
