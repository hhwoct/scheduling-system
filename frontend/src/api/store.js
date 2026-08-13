import request from '../utils/request'
import { API_ROUTES } from '../constants/api'

export function getCurrentStore() {
  return request.get(API_ROUTES.STORE.CURRENT).then(res => res.data)
}