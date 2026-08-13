import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

// 员工提交换班
export function submitSwap(data) {
  return request.post(API_ROUTES.SWAP.BASE, data).then(res => res.data)
}

// 员工我的换班（管理员可用 employeeNo 预览）
export function getMySwaps(employeeNo) {
  return request.get(API_ROUTES.SWAP.MINE, { params: { employeeNo } }).then(res => res.data)
}

// 员工获取可换班同事
export function getSwapCandidates(planId, swapDate) {
  return request.post(API_ROUTES.SWAP.CANDIDATES, { planId, swapDate }).then(res => res.data)
}

// 管理员：换班审批列表
export function getSwapReviewList(status) {
  return request.get(API_ROUTES.SWAP.REVIEW, { params: { status } }).then(res => res.data)
}

// 管理员：审批（通过/驳回）
export function reviewSwap(id, data) {
  return request.put(API_ROUTES.SWAP.REVIEW_DETAIL(id), data).then(res => res.data)
}