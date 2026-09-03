import request from './request'
import { API_ROUTES } from '../constants/api'

export function login(data) {
  return request.post(API_ROUTES.AUTH.LOGIN, data).then((res) => res.data)
}

export function getCurrentUser() {
  return request.get(API_ROUTES.AUTH.ME).then((res) => res.data)
}

export function forgotPassword(data) {
  return request.post(API_ROUTES.AUTH.FORGOT_PASSWORD, data).then((res) => res.data)
}

// 登录后自助修改密码：成功后旧 Token 全部失效，需重新登录
export function changePassword(data) {
  return request.post(API_ROUTES.AUTH.CHANGE_PASSWORD, data).then((res) => res.data)
}
