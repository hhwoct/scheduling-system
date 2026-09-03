import axios from 'axios'
import { showToast } from 'vant'
import router from '../router'
import { API_BASE_URL, REQUEST_TIMEOUT } from '../constants/config'

const request = axios.create({
  baseURL: API_BASE_URL,
  timeout: REQUEST_TIMEOUT || 15000
})

request.interceptors.request.use((config) => {
  const token = localStorage.getItem('shift_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 401 重定向防抖（与 PC 端一致的约定）
let isRedirecting = false

request.interceptors.response.use(
  (response) => {
    const res = response.data
    if (!res || typeof res !== 'object') {
      return res
    }
    if (res.success === false) {
      showToast(res.message || '操作失败')
      const bizError = new Error(res.message || '操作失败')
      bizError.bizCode = res.errorCode || null
      return Promise.reject(bizError)
    }
    return res
  },
  (error) => {
    const status = error.response?.status
    const message = error.response?.data?.message || error.message || '网络错误'
    if (status === 401) {
      localStorage.removeItem('shift_token')
      localStorage.removeItem('shift_role')
      localStorage.removeItem('shift_username')
      localStorage.removeItem('shift_store_id')
      window.dispatchEvent(new CustomEvent('auth:unauthorized'))
      // 避免并发请求重复跳转
      if (router.currentRoute.value.path !== '/login' && !isRedirecting) {
        isRedirecting = true
        router.push('/login')
        setTimeout(() => {
          isRedirecting = false
        }, 1000)
      }
    }
    // 错误标准化，让调用方拿到统一 Error 对象
    const normalizedError = new Error(message)
    normalizedError.status = status
    normalizedError.code = error.code
    normalizedError.original = error
    // 调用方可通过 config.skipErrorToast 声明自行降级处理
    if (error.config?.skipErrorToast !== true && status !== 401) {
      showToast(message)
    }
    return Promise.reject(normalizedError)
  }
)

export default request
