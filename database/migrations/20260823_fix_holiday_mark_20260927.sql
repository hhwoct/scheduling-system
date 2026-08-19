-- ============================================================
-- 20260823: 修正 20260818 中 2026-09-27（调休补班日）被误标为法定节假日的问题
-- 对「已执行过 20260818 修正前的版本」的库生效；幂等（条件更新）。
-- 新库若由 init_shift_mvp.sql 建库则已包含正确口径，本脚本执行无副作用。
-- ============================================================
USE shift_mvp;

-- 同时修正 day_type：对跑过「修复前 20260818」的库，09-27 除 is_legal_holiday=1 外，
-- 其 day_type 也残留 'HOLIDAY'（周日被旧口径判为周末），需一并改回 WORKDAY。
-- 守卫用 (is_legal_holiday=1 OR day_type='HOLIDAY')：同时覆盖「已执行过本迁移旧版
-- （只清了 is_legal_holiday 未清 day_type）」的库，幂等。
UPDATE date_parameters
SET is_legal_holiday = 0, day_type = 'WORKDAY'
WHERE work_date = '2026-09-27'
  AND (is_legal_holiday = 1 OR day_type = 'HOLIDAY');
