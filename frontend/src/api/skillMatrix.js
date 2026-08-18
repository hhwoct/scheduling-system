import request from '../utils/request'

// 门店技能等级总览（员工 × 工作站矩阵，兼职不参与评级）
export function getSkillMatrix() {
  return request.get('/skill-matrix').then(res => res.data)
}

// 单格技能修改（管理员/店长）
export function updateSkillCell(data) {
  return request.put('/skill-matrix/cell', data).then(res => res.data)
}

// 通岗设置（1=通岗，0=取消）
export function setGeneralist(data) {
  return request.put('/skill-matrix/generalist', data).then(res => res.data)
}
