import request from './request'
import { API_ROUTES } from '../constants/api'

// 通知列表（分页）：返回 { items, total, page, pageSize }
export function getNotifications(page = 1, pageSize = 20) {
  return request.get(API_ROUTES.NOTIFICATIONS.BASE, { params: { page, pageSize } }).then((res) => res.data)
}

// 未读数量：返回 { count }
export function getUnreadCount() {
  return request.get(API_ROUTES.NOTIFICATIONS.UNREAD_COUNT).then((res) => res.data)
}

// 标记单条已读
export function markNotificationRead(id) {
  return request.put(API_ROUTES.NOTIFICATIONS.READ(id)).then((res) => res.data)
}

// 全部标记已读
export function markAllNotificationsRead() {
  return request.put(API_ROUTES.NOTIFICATIONS.READ_ALL).then((res) => res.data)
}
