export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'
export const REQUEST_TIMEOUT = Number(import.meta.env.VITE_REQUEST_TIMEOUT || 15000)
// 超管账号用户名（审计日志等敏感页面仅该账号可见，须与后端 SuperAdminUsername 配置一致）
export const SUPER_ADMIN_USERNAME = import.meta.env.VITE_SUPER_ADMIN_USERNAME || 'admin'
