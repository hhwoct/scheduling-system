import request from '../utils/request'

// 获取通知列表
export function getNotifications() {
  return request.get('/notifications').then(res => res.data)
}

// 获取未读数量
export function getUnreadCount() {
  return request.get('/notifications/unread-count').then(res => res.data)
}

// 标记已读
export function markAsRead(id) {
  return request.put(`/notifications/${id}/read`).then(res => res.data)
}

// 全部标记已读
export function markAllAsRead() {
  return request.put('/notifications/read-all').then(res => res.data)
}