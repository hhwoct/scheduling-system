-- ============================================================
-- 20260819: 人数需求支持「最少/最好」两档（required_count=最少, ideal_count=最好）
-- 语义：单元格一个数字 N = 最少 N 且最好 N；写成 (M,N) 表示最少 M 最好 N。
-- 内容：staffing_requirements 新增 ideal_count 列（无默认值，避免 DEFAULT 0 与
--       CHECK(ideal_count>=required_count) 冲突），并回填为 required_count。
-- 幂等性：加列/删默认值/加 CHECK 均带 information_schema 存在性守卫，重复执行无副作用；
--         回填为无条件修复（required_count>0 且 ideal_count=0 的行），幂等。
-- 顺序依赖：本脚本须在 20260818_add_weekend_staffing.sql 之后执行。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260819_add_ideal_count.sql
-- ============================================================
USE shift_mvp;

-- 1. 判断列是否已存在
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'staffing_requirements'
    AND COLUMN_NAME = 'ideal_count'
);

-- 2. 加列（不加 DEFAULT：若带 DEFAULT 0，则「只写 required_count」的插入会得到 ideal=0，
--    与 CHECK(ideal>=required) 冲突而失败）
SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE staffing_requirements ADD COLUMN ideal_count INT NOT NULL AFTER required_count',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 3. 去掉默认值（幂等守卫：仅当列仍带显式默认值时执行，兼容旧版本迁移曾加 DEFAULT 0 的库）
SET @has_default := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'staffing_requirements'
    AND COLUMN_NAME = 'ideal_count'
    AND COLUMN_DEFAULT IS NOT NULL
);
SET @ddl := IF(@has_default = 1,
  'ALTER TABLE staffing_requirements ALTER COLUMN ideal_count DROP DEFAULT',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 4. 回填（无条件修复，放在加 CHECK 之前，幂等）：最好人数 = 最少人数。
--    不用 @col_exists 作门（避免 ALTER 成功/回填失败后无法恢复）；
--    只修复「required_count>0 且 ideal_count=0」这类会违反 CHECK 的不一致行。
UPDATE staffing_requirements
SET ideal_count = required_count
WHERE required_count > 0 AND ideal_count = 0;

-- 5. CHECK 约束（MySQL 8.0.16+）：最好人数不得小于最少人数。带存在性守卫，幂等。
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
