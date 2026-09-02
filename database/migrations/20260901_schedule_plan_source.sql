-- 20260901 排班计划来源标记：REAL=导入的真实班表（对比基线），ALGO=系统算法生成
-- 幂等：重复执行安全
SET @col := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'schedule_plans' AND COLUMN_NAME = 'source');
SET @sql := IF(@col = 0,
  'ALTER TABLE schedule_plans ADD COLUMN source VARCHAR(20) NOT NULL DEFAULT ''ALGO'' COMMENT ''REAL=导入真实班表, ALGO=算法生成'' AFTER status',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
