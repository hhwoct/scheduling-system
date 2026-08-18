-- ============================================================
-- 20260823: shift_swaps.target_employee_id 索引（老库补建）
-- 换班审批/通知按同伴员工查询（target_employee_id），主查询列此前无索引。
-- 幂等：通过 information_schema.STATISTICS 判断索引是否存在，存在则跳过。
-- 新库（init_shift_mvp.sql / 20260807 已内置该索引）执行本脚本无副作用。
-- ============================================================
USE shift_mvp;

SET @idx_exists := (
  SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'shift_swaps'
    AND INDEX_NAME = 'idx_shift_swaps_target'
);

SET @ddl := IF(@idx_exists = 0,
  'ALTER TABLE shift_swaps ADD INDEX idx_shift_swaps_target (target_employee_id)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
