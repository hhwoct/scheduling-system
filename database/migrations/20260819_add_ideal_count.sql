-- ============================================================
-- 20260819: 人数需求支持「最少/最好」两档（required_count=最少, ideal_count=最好）
-- 语义：单元格一个数字 N = 最少 N 且最好 N；写成 (M,N) 表示最少 M 最好 N。
-- 内容：staffing_requirements 新增 ideal_count 列，并回填为 required_count。
-- 幂等性：重复执行无副作用（列已存在时仅重复 UPDATE）。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260819_add_ideal_count.sql
-- ============================================================
USE shift_mvp;

ALTER TABLE staffing_requirements
  ADD COLUMN ideal_count INT NOT NULL DEFAULT 0 AFTER required_count;

-- 历史数据：最好人数 = 最少人数
UPDATE staffing_requirements
SET ideal_count = required_count
WHERE ideal_count = 0;
