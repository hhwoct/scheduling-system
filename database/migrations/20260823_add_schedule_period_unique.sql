-- ============================================================
-- 20260823: 排班计划周期唯一约束（幂等兜底）
-- 应用层 GenerateAsync 已有同门店同周期预检（check-then-act），本约束为数据库兜底，
-- 防止并发双击生成重复计划。
-- 幂等性：先查 information_schema，约束已存在（如 init 建的新库已内置）则跳过；
--         并用 GET_LOCK/RELEASE_LOCK 串行化「检查 + DDL」，防止并发执行时双跑都判定不存在。
-- 注意：对老库执行时，若库中存在同门店同周期的重复计划，会报 Duplicate entry，
--       此时需先人工清理重复计划再重试。
-- ============================================================
USE shift_mvp;

-- 命名锁串行化：GET_LOCK 成功返回 1；超时(30s)返回 0（极端情况下放弃加锁直接执行，仅作提示）。
SELECT GET_LOCK('shift_mvp.migrate_ux_schedule_plans_period', 30);

SET @constraint_exists := (
  SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'schedule_plans'
    AND CONSTRAINT_NAME = 'ux_schedule_plans_period'
    AND CONSTRAINT_TYPE = 'UNIQUE'
);

SET @ddl := IF(@constraint_exists = 0,
  'ALTER TABLE schedule_plans ADD CONSTRAINT ux_schedule_plans_period UNIQUE (store_id, start_date, end_date)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SELECT RELEASE_LOCK('shift_mvp.migrate_ux_schedule_plans_period');
