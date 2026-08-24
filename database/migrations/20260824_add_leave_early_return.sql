-- 请假表新增"是否提前返岗"标记：员工提前返岗时置 1（原结束日期仅缩短，无额外记录）
-- 审查修复（H7）：加存在性守卫保证幂等——init_shift_mvp.sql 已吸收本列，
-- 新装库按"init + 20260823 起迁移"路径执行时不会因重复列报 ERROR 1060。
USE shift_mvp;

SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'leave_requests'
    AND COLUMN_NAME = 'early_returned'
);

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE leave_requests ADD COLUMN early_returned TINYINT(1) NOT NULL DEFAULT 0 COMMENT ''是否提前返岗（1=是）'' AFTER review_remark',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
