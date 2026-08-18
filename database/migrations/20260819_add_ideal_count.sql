-- ============================================================
-- 20260819: 人数需求支持「最少/最好」两档（required_count=最少, ideal_count=最好）
-- 语义：单元格一个数字 N = 最少 N 且最好 N；写成 (M,N) 表示最少 M 最好 N。
-- 内容：staffing_requirements 新增 ideal_count 列，并回填为 required_count。
-- 幂等性：通过 information_schema 判断列是否已存在，重复执行无副作用；
--         历史数据回填仅在「本次新增列」时执行一次，避免覆盖后续有意设为 0 的理想人数。
-- 顺序依赖：本脚本须在 20260818_add_weekend_staffing.sql 之后执行。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260819_add_ideal_count.sql
-- ============================================================
USE shift_mvp;

-- 判断列是否已存在（幂等）
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'staffing_requirements'
    AND COLUMN_NAME = 'ideal_count'
);

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE staffing_requirements ADD COLUMN ideal_count INT NOT NULL DEFAULT 0 AFTER required_count',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 历史数据回填：最好人数 = 最少人数（仅当本次新增列时执行）。
-- 不用 ideal_count=0 作「未回填」哨兵：列刚新增时所有行均为 DEFAULT 0，
-- 直接按 required_count 无条件回填即可（@col_exists=0 保证只执行一次）。
UPDATE staffing_requirements
SET ideal_count = required_count
WHERE @col_exists = 0;

-- CHECK 约束（MySQL 8.0.16+）：最好人数不得小于最少人数。带存在性守卫，幂等。
SET @chk_exists := (
  SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME = 'staffing_requirements'
    AND CONSTRAINT_NAME = 'chk_ideal_ge_required'
);

SET @ddl := IF(@chk_exists = 0,
  'ALTER TABLE staffing_requirements ADD CONSTRAINT chk_ideal_ge_required CHECK (ideal_count >= required_count)',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
