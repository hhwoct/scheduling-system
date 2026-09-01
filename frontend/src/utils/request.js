import axios from 'axios'
import { ElMessage } from 'element-plus'
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

// P3-19: 401 重定向防抖
let isRedirecting = false

// P3-20: 响应拦截器处理业务错误与空值防护
request.interceptors.response.use(
  (response) => {
    const res = response.data
    if (!res || typeof res !== 'object') {
      return res
    }
    if (res.success === false) {
      ElMessage.error(res.message || '操作失败')
      return Promise.reject(new Error(res.message || '操作失败'))
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
      localStorage.removeItem('shift_token_expires_at')
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
    // P3-17: 错误标准化，让调用方拿到统一 Error 对象
    const normalizedError = new Error(message)
    normalizedError.status = status
    normalizedError.code = error.code
    normalizedError.original = error
    // 调用方可通过 config.skipErrorToast 声明自行降级处理(如门店总览
    // 接口未部署时页面内温和提示),此时不再弹全局错误
    if (error.config?.skipErrorToast !== true) {
      ElMessage.error(message)
    }
    return Promise.reject(normalizedError)
  }
)

export default request