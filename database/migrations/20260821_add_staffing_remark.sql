-- ============================================================
-- 20260821: 人数需求格子支持备注（框选批量修改时可附带）
-- 内容：staffing_requirements 新增 remark 列（最长 200 字符，可空）。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260821_add_staffing_remark.sql
-- ============================================================
USE shift_mvp;

ALTER TABLE staffing_requirements
  ADD COLUMN remark VARCHAR(200) NULL AFTER ideal_count;
