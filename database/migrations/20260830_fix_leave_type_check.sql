-- ============================================================
-- 20260830: 请假类型 CHECK 约束补 OTHER（审查修复 P1）
--
-- 背景：接口文档与前端均支持 OTHER（其他）类型，但 init 的
--       chk_leave_type 只允许 PERSONAL/SICK/ANNUAL，老库提交
--       「其他」请假会撞 CHECK 约束报 500。
-- 幂等：先按信息模式判断约束存在再删除，随后重建（含 OTHER）。
-- ============================================================
USE shift_mvp;

SET @exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'leave_requests'
    AND CONSTRAINT_NAME = 'chk_leave_type');
SET @ddl := IF(@exists > 0,
  'ALTER TABLE leave_requests DROP CHECK chk_leave_type',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

ALTER TABLE leave_requests
  ADD CONSTRAINT chk_leave_type CHECK (leave_type IN ('PERSONAL', 'SICK', 'ANNUAL', 'OTHER'));
