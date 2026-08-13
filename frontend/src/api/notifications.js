import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

// 获取通知列表（P3-36: 支持分页，管理员可用 employeeNo 预览）
export function getNotifications(params) {
  return request.get(API_ROUTES.NOTIFICATIONS.BASE, { params }).then(res => res.data)
}

// 获取未读数量（管理员可用 employeeNo 预览）
export function getUnreadCount(employeeNo) {
  return request.get(API_ROUTES.NOTIFICATIONS.UNREAD_COUNT, { params: { employeeNo } }).then(res => res.data)
}

// 标记已读
export function markAsRead(id) {
  return request.put(API_ROUTES.NOTIFICATIONS.READ(id)).then(res => res.data)
}

// 全部标记已读
export function markAllAsRead() {
  return request.put(API_ROUTES.NOTIFICATIONS.READ_ALL).then(res => res.data)
}