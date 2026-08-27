-- 2026-08-27 修复：闭店空窗 06:00 的误配需求与历史幽灵时段
-- 背景：班表时间轴为 13:00 开门 → 次日 06:00 打烊（05:30 为最后一格）。
--       人数需求中保洁岗(6)在 06:00 有 required_count=1 的误配（打烊后时段），
--       引擎据此生成 D 临时班次，导致每个计划每天多 1 条 time_slot='06:00:00'、
--       界面不可见的幽灵时段记录。引擎侧已同步加防御（闭店时段 06:00~12:30 忽略）。
-- 本迁移：清零误配需求 + 清理历史幽灵明细/问题/汇总。幂等，可重复执行。

-- 1) 清零打烊时段 06:00 的误配需求（仅保洁岗存在）
UPDATE staffing_requirements
SET required_count = 0, ideal_count = 0
WHERE time_slot = '06:00:00' AND (required_count > 0 OR ideal_count > 0);

-- 2) 删除幽灵时段明细（所有计划的 06:00 段）
DELETE FROM schedule_results WHERE time_slot = '06:00:00';

-- 3) 删除该时段的陈旧问题（缺口类，源自误配需求）
DELETE FROM schedule_issues WHERE time_slot = '06:00:00';

-- 4) 删除因幽灵时段而残留的空壳汇总（当日仅剩 06:00 一条明细、start=06:00/end=06:30/0.5h）
--    其余员工的汇总在生成时即未计入 06:00，删除明细后与汇总保持一致，无需改动。
DELETE FROM schedule_summaries
WHERE start_time = '06:00:00' AND end_time = '06:30:00' AND work_hours = 0.5;
