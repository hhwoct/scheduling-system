/**
 * 图表 / SVG 用色板
 *
 * ECharts 和 SVG presentation attribute 无法消费 CSS 变量,只能走 JS。
 * 这里是这类场景的唯一来源,取值与 styles/tokens.css 的 --app-chart-* 对齐。
 * 纯 CSS 场景一律用变量,不要 import 这个文件。
 *
 * Apple 风格改造(2026-10):语义色切换到 iOS 系统色板(蓝/绿/橙/红/灰)
 */

/** 分类色板,按顺序循环取用 */
export const CHART_COLORS = [
  '#007aff', // primary(iOS 蓝)
  '#34c759', // success(iOS 绿)
  '#ff9500', // warning(iOS 橙)
  '#ff3b30', // danger(iOS 红)
  '#8e8e93', // info(iOS 灰)
  '#b37feb',
  '#ff85c0',
  '#36cfc9'
]

/** 问题类型分布饼图:红->橙->蓝->绿->灰,按严重度递减 */
export const SEVERITY_COLORS = ['#ff3b30', '#ff9500', '#007aff', '#34c759', '#8e8e93']

/** 兜底灰,用于「其他」分组与取不到色时 */
export const FALLBACK_COLOR = '#c7c7cc'

/** 图表内的文字与网格线,对齐 Element 文本层级 */
export const CHART_INK = {
  primary: '#1d1d1f',
  regular: '#3a3a3c',
  secondary: '#6e6e73',
  splitLine: '#ececf0',
  onColor: '#ffffff'
}

/** 按下标取色,越界自动循环 */
export function colorAt(index, colors = CHART_COLORS) {
  return colors[index % colors.length] || FALLBACK_COLOR
}
