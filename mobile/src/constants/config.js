export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'
export const REQUEST_TIMEOUT = Number(import.meta.env.VITE_REQUEST_TIMEOUT || 15000)
// 店长账号用户名（与后端 StoreManagerUsername 配置一致，后续审批页提示用）
export const STORE_MANAGER_USERNAME = import.meta.env.VITE_STORE_MANAGER_USERNAME || 'E001'
// 超管账号用户名（与后端 SuperAdminUsername 配置一致）
export const SUPER_ADMIN_USERNAME = import.meta.env.VITE_SUPER_ADMIN_USERNAME || 'admin'
