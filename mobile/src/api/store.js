import request from './request'
import { API_ROUTES } from '../constants/api'

export function getCurrentStore() {
  return request.get(API_ROUTES.STORE.CURRENT).then((res) => res.data)
}

// 全部门店列表（仅超管可用）
export function getStores() {
  return request.get(API_ROUTES.STORE.ALL).then((res) => res.data)
}
