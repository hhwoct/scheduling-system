import request from '../utils/request'

export function login(data) {
  return request.post('/auth/login', data).then(res => res.data)
}

export function getCurrentUser() {
  return request.get('/auth/me').then(res => res.data)
}

export function forgotPassword(data) {
  return request.post('/auth/forgot-password', data).then(res => res.data)
}
