-- ============================================================
-- 20260830: 人数需求 ideal_count 口径对齐（审查修复 P1-4）
--
-- 背景：新装库（init 路径）ideal_count 无 CHECK，与老库升级路径
--       （20260819 加了 chk_ideal_ge_required）不一致，导致新装库
--       可写入 ideal < required 的非法数据。
-- 步骤：1) 归正存量违规数据（ideal 抬到 required）；2) 幂等补 CHECK。
-- ============================================================
USE shift_mvp;

UPDATE staffing_requirements
SET ideal_count = required_count, updated_at = CURRENT_TIMESTAMP
WHERE ideal_count < required_count;

SET @exists := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'staffing_requirements'
    AND CONSTRAINT_NAME = 'chk_ideal_ge_required');
SET @ddl := IF(@exists = 0,
  'ALTER TABLE staffing_requirements ADD CONSTRAINT chk_ideal_ge_required CHECK (ideal_count >= required_count)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
