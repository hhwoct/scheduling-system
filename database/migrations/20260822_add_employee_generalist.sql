-- ============================================================
-- 20260822: 员工「通岗」标记
-- 通岗 = 大部分楼面工作都能做（传送/保洁/咨客/服务等低技能岗位）。
-- 勾选后系统自动为这些岗位写入至少 3 分的技能；取消时清 0（技能行保留）。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260822_add_employee_generalist.sql
-- ============================================================
USE shift_mvp;

ALTER TABLE employees
  ADD COLUMN is_generalist TINYINT NOT NULL DEFAULT 0 COMMENT '是否通岗' AFTER is_parttime;
