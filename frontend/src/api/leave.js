import request from '../utils/request'

// 员工提交请假
export function submitLeave(data) {
  return request.post('/leave-requests', data).then(res => res.data)
}

// 员工我的请假
export function getMyLeaves() {
  return request.get('/leave-requests/mine').then(res => res.data)
}

// 管理员：请假审批列表
export function getLeaveReviewList(status) {
  return request.get('/leave-requests/review', { params: { status } }).then(res => res.data)
}

// 管理员：审批（通过/驳回）
export function reviewLeave(id, data) {
  return request.put(`/leave-requests/${id}/review`, data).then(res => res.data)
}
