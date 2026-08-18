import request from '../utils/request'

// AI 文档识别配置（DeepSeek）
export function getAiConfig() {
  return request.get('/ai/config').then(res => res.data)
}

export function saveAiConfig(data) {
  return request.put('/ai/config', data).then(res => res.data)
}

export function testAi() {
  return request.post('/ai/test', null, { timeout: 130000 }).then(res => res.data)
}

// AI 识别人数需求文档：kind='sheet' 传 rows；kind='image' 传 imageBase64/imageMimeType
export function aiParseRequirementDoc(data) {
  return request.post('/ai/parse-requirement-doc', data, { timeout: 130000 }).then(res => res.data)
}
