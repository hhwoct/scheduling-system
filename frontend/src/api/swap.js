import request from '../utils/request'

// 员工提交换班
export function submitSwap(data) {
  return request.post('/shift-swaps', data).then(res => res.data)
}

// 员工我的换班
export function getMySwaps() {
  return request.get('/shift-swaps/mine').then(res => res.data)
}

// 员工获取可换班同事
export function getSwapCandidates(planId, swapDate) {
  return request.post('/shift-swaps/candidates', { planId, swapDate }).then(res => res.data)
}

// 管理员：换班审批列表
export function getSwapReviewList(status) {
  return request.get('/shift-swaps/review', { params: { status } }).then(res => res.data)
}

// 管理员：审批（通过/驳回）
export function reviewSwap(id, data) {
  return request.put(`/shift-swaps/${id}/review`, data).then(res => res.data)
}