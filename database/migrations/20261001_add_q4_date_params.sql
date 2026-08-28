-- ============================================================
-- 20261001: 补充 2026 年 Q4（10~12 月）日期参数（国庆假期 + 调休补班）
--
-- 背景：date_parameters 仅覆盖到 2026-09（init 内置 + 20260806 迁移），
--       10 月起无任何记录，生成排班（如 2026-10-01 ~ 2026-10-07、下月 11/12 月、
--       跨月「未来一周」）时报
--       「所选日期范围缺少日期参数（节假日/工作日配置），当前仅覆盖 0/7 天」。
--
-- 口径（与 init 的 2026-09 口径一致，源自国务院 2026 年放假安排）：
--   1. 国庆法定节假日：2026-10-01（周四）~ 10-07（周三），共 7 天
--      → HOLIDAY，is_legal_holiday=1；
--   2. 调休补班日：2026-10-10（周六）→ WORKDAY（与 09-27 补班同属中秋国庆调休安排）；
--   3. 2026-11 / 2026-12 无法定节假日，其余日期一律：
--      周五/周六 → WEEKEND，周日~周四 → WORKDAY；
--   4. 节前日：2026-09-30（周三，国庆前一日）→ is_holiday_eve=1
--      （9 月数据由 init/09 迁移写入，本脚本 UPDATE 补标）。
--
-- 幂等：INSERT ... ON DUPLICATE KEY UPDATE（按 store_id+work_date 唯一键）；
--       节前日 UPDATE 带守卫可重复执行。新环境 init + 全部迁移、老库按序迁移两种路径均可安全执行。
-- 已知边界：2027-01-01（元旦）起仍未配置，跨年周期（如 12 月下旬「未来一周」）需另行补充。
-- ============================================================
USE shift_mvp;

INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve)
SELECT 1, d, DAYOFWEEK(d),
  CASE
    WHEN d BETWEEN '2026-10-01' AND '2026-10-07' THEN 'HOLIDAY'
    WHEN d = '2026-10-10' THEN 'WORKDAY'
    WHEN DAYOFWEEK(d) IN (6, 7) THEN 'WEEKEND'
    ELSE 'WORKDAY'
  END,
  CASE WHEN d BETWEEN '2026-10-01' AND '2026-10-07' THEN 1 ELSE 0 END,
  0
FROM (
  WITH RECURSIVE seq AS (
    SELECT 0 AS n
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 91   -- 2026-10-01 + 91 天 = 2026-12-31（共 92 天）
  )
  SELECT DATE('2026-10-01') + INTERVAL n DAY AS d FROM seq
) dates
ON DUPLICATE KEY UPDATE
  week_day = VALUES(week_day),
  day_type = VALUES(day_type),
  is_legal_holiday = VALUES(is_legal_holiday),
  is_holiday_eve = VALUES(is_holiday_eve);

-- 国庆前一日（09-30）补标节前日（幂等：仅当未标时更新）
UPDATE date_parameters
SET is_holiday_eve = 1
WHERE store_id = 1 AND work_date = '2026-09-30' AND is_holiday_eve = 0;
