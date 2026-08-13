import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

export function login(data) {
  return request.post(API_ROUTES.AUTH.LOGIN, data).then(res => res.data)
}

export function getCurrentUser() {
  return request.get(API_ROUTES.AUTH.ME).then(res => res.data)
}

export function forgotPassword(data) {
  return request.post(API_ROUTES.AUTH.FORGOT_PASSWORD, data).then(res => res.data)
}