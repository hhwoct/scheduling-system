/**
 * 图表 / SVG 用色板
 *
 * ECharts 和 SVG presentation attribute 无法消费 CSS 变量,只能走 JS。
 * 这里是这类场景的唯一来源,取值与 styles/tokens.css 的 --app-chart-* 对齐。
 * 纯 CSS 场景一律用变量,不要 import 这个文件。
 */

/** 分类色板,按顺序循环取用 */
export const CHART_COLORS = [
  '#409eff', // primary
  '#67c23a', // success
  '#e6a23c', // warning
  '#f56c6c', // danger
  '#909399', // info
  '#b37feb',
  '#ff85c0',
  '#36cfc9'
]

/** 问题类型分布饼图:红->橙->蓝->绿->灰,按严重度递减 */
export const SEVERITY_COLORS = ['#f56c6c', '#e6a23c', '#409eff', '#67c23a', '#909399']

/** 兜底灰,用于「其他」分组与取不到色时 */
export const FALLBACK_COLOR = '#c0c4cc'

/** 图表内的文字与网格线,对齐 Element 文本层级 */
export const CHART_INK = {
  primary: '#303133',
  regular: '#606266',
  secondary: '#909399',
  splitLine: '#ebeef5',
  onColor: '#ffffff'
}

/** 按下标取色,越界自动循环 */
export function colorAt(index, colors = CHART_COLORS) {
  return colors[index % colors.length] || FALLBACK_COLOR
}
