import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

export function getCurrentStore() {
  return request.get(API_ROUTES.STORE.CURRENT).then(res => res.data)
}

// 门店总览(超管):旗下所有门店 + 每店核心指标
// 路由常量待 constants/api.js 收敛时再迁过去
// 接口未部署(404)时静默失败,由 DashboardView 做页面内降级提示
export function getStores() {
  return request.get('/stores', { skipErrorToast: true }).then(res => res.data)
}