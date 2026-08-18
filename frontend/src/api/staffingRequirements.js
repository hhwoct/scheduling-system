import request from '../utils/request'

// 人数需求配置：平日/周末/节假日 × 工作站 × 30 分钟时段
export function getStaffingRequirements(dayType) {
  return request
    .get('/staffing-requirements', { params: dayType ? { dayType } : {} })
    .then(res => res.data)
}

// dayType: WORKDAY | WEEKEND | HOLIDAY；entries: [{ workstationId, timeSlot, requiredCount }]
export function saveStaffingRequirements(data) {
  return request.put('/staffing-requirements', data).then(res => res.data)
}

// 周期需求预览（按营业日口径统计各日期类型的天数/人·时/峰值并发）
export function getStaffingRequirementPreview(params) {
  return request.get('/staffing-requirements/preview', { params }).then(res => res.data)
}
