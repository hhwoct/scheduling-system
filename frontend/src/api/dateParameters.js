import request from '../utils/request'

// 日期参数（节假日/工作日配置）：按月份查询/批量保存/按周末规则补全
// month: 'yyyy-MM'；返回 { year, month, items: [{ workDate, weekDay, dayType, isLegalHoliday, isHolidayEve, isConfigured }] }
export function getDateParameters(month) {
  return request.get('/date-parameters', { params: { month } }).then(res => res.data)
}

// items: [{ workDate: 'yyyy-MM-dd', dayType: 'WORKDAY|WEEKEND|HOLIDAY', isLegalHoliday, isHolidayEve }]
export function saveDateParameters(items) {
  return request.put('/date-parameters', { items }).then(res => res.data)
}

// 按周末规则补全从下月起连续 months 个月（已配置日期不覆盖）
export function generateDateParameters(months) {
  return request.post('/date-parameters/generate', { months }).then(res => res.data)
}
