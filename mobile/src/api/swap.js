import request from './request'
import { API_ROUTES } from '../constants/api'

// 可换班的已发布排班计划列表
export function getSwapPlans() {
  return request.get(API_ROUTES.SWAP.PLANS).then((res) => res.data)
}

// 指定日期可换班同事（当天休息的员工）
export function getSwapCandidates(planId, swapDate) {
  return request.post(API_ROUTES.SWAP.CANDIDATES, { planId, swapDate }).then((res) => res.data)
}

// 提交换班申请：{ planId, targetEmployeeId, swapDate, reason }
export function createSwap(data) {
  return request.post(API_ROUTES.SWAP.BASE, data).then((res) => res.data)
}

// 我的换班列表
export function getMySwaps() {
  return request.get(API_ROUTES.SWAP.MINE).then((res) => res.data)
}
