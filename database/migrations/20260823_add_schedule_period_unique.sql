-- ============================================================
-- 20260823: 排班计划周期唯一约束（幂等兜底）
-- 应用层 GenerateAsync 已有同门店同周期预检（check-then-act），本约束为数据库兜底，
-- 防止并发双击生成重复计划。
-- 幂等性：先查 information_schema，约束已存在（如 init 建的新库已内置）则跳过。
-- 注意：对老库执行时，若库中存在同门店同周期的重复计划，会报 Duplicate entry，
--       此时需先人工清理重复计划再重试。
-- ============================================================
USE shift_mvp;

SET @constraint_exists := (
  SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'schedule_plans'
    AND CONSTRAINT_NAME = 'ux_schedule_plans_period'
);

SET @ddl := IF(@constraint_exists = 0,
  'ALTER TABLE schedule_plans ADD CONSTRAINT ux_schedule_plans_period UNIQUE (store_id, start_date, end_date)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
