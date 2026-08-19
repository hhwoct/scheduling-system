-- ============================================================
-- 20260821: 人数需求格子支持备注（框选批量修改时可附带）
-- 内容：staffing_requirements 新增 remark 列（最长 200 字符，可空）。
-- 约定：空备注统一存 NULL（应用层 StaffingRequirementService 将空串归一化为 NULL，不存 ''）；
--       200 字符上限由应用层校验（DB VARCHAR(200) 仅作兜底，不加 CHECK）。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260821_add_staffing_remark.sql
-- ============================================================
USE shift_mvp;

ALTER TABLE staffing_requirements
  ADD COLUMN remark VARCHAR(200) NULL AFTER ideal_count;
