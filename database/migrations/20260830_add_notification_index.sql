-- ============================================================
-- 20260830: notifications 未读查询复合索引（审查修复 P2-3）
--
-- 背景：未读列表查询按 (store_id, is_read) + receiver_employee_id 过滤，
--       现有索引需回表，员工多时扫描放大。补 (store_id, receiver_employee_id, is_read)。
-- ============================================================
USE shift_mvp;

SET @exists := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'notifications'
    AND INDEX_NAME = 'idx_notifications_store_receiver_read');
SET @ddl := IF(@exists = 0,
  'CREATE INDEX idx_notifications_store_receiver_read ON notifications (store_id, receiver_employee_id, is_read)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
