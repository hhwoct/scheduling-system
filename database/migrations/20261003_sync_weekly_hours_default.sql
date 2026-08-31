-- 同步「最大周工时」：全局规则 max_weekly_hours 与员工个人周工时上限
-- 1) 员工表新增「跟随默认」标记列（幂等：列已存在则跳过）
-- 2) 一次性把全局规则与全部在职员工统一为 60（用户指定），并标记为跟随默认
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20261003_sync_weekly_hours_default.sql
USE shift_mvp;

-- 1. 幂等加列
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'employees'
    AND COLUMN_NAME = 'weekly_hours_follow_default'
);
SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE employees ADD COLUMN weekly_hours_follow_default TINYINT NOT NULL DEFAULT 1 COMMENT ''周工时上限是否跟随全局规则（1=跟随默认，0=个人自定义）'' AFTER max_weekly_hours',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2. 全局「最大周工时」规则恢复到 60
UPDATE rule_configs
   SET rule_value = '60',
       updated_at = NOW()
 WHERE rule_key = 'max_weekly_hours';

-- 3. 在职员工统一为 60 并跟随默认（清空个人覆盖）
UPDATE employees
   SET max_weekly_hours = 60.00,
       weekly_hours_follow_default = 1,
       updated_at = NOW()
 WHERE status = 1;
