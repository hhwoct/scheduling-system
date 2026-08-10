import request from '../utils/request'

export function getCurrentStore() {
  return request.get('/stores/current').then(res => res.data)
}
