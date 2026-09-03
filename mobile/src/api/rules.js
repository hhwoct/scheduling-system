import request from './request'
import { API_ROUTES } from '../constants/api'

// 规则列表：admin 返回全局默认规则；店长返回生效合并规则
export function getRules() {
  return request.get(API_ROUTES.RULES.BASE).then((res) => res.data)
}

// 保存规则：{ ruleValue, status, version? }
export function updateRule(id, data) {
  return request.put(API_ROUTES.RULES.DETAIL(id), data).then((res) => res.data)
}
