USE shift_mvp;

-- ============================================================
-- P2 数据模型修复综合迁移
-- 包含：节假日/日期参数/约束/索引/重复数据清理
-- 注意：MySQL DDL 隐式提交，不使用 START TRANSACTION
-- ============================================================

-- ===== P2-7: 清理 employee_skills 重复数据 =====
DELETE e1 FROM employee_skills e1
INNER JOIN employee_skills e2
  ON e1.employee_id = e2.employee_id
  AND e1.workstation_id = e2.workstation_id
  AND e1.id > e2.id;

-- ===== P2-7: 清理 employees 重复员工号（保留最小 id 的活跃记录）=====
UPDATE employees e1
SET status = 0
WHERE EXISTS (
    SELECT 1 FROM employees e2
    WHERE e2.store_id = e1.store_id
    AND e2.employee_no = e1.employee_no
    AND e2.id < e1.id
    AND e2.status = 1
);

-- ===== P2-9: 换班请求唯一约束（PENDING 去重）=====
-- 生成列：仅 PENDING 状态记录非 NULL
ALTER TABLE shift_swaps
  ADD COLUMN pending_flag TINYINT
  GENERATED ALWAYS AS (IF(status = 'PENDING', 1, NULL)) STORED;

CREATE UNIQUE INDEX ux_shift_swaps_pending
  ON shift_swaps(requester_employee_id, target_employee_id, plan_id, swap_date, pending_flag);

-- ===== P2-10: Leave 表日期范围约束 =====
ALTER TABLE leave_requests
  ADD CONSTRAINT chk_leave_date_range
  CHECK (start_date <= end_date);

ALTER TABLE leave_requests
  ADD CONSTRAINT chk_leave_max_days
  CHECK (DATEDIFF(end_date, start_date) <= 30);

-- ===== P2-13: Leave 表 status 索引 =====
CREATE INDEX idx_leave_requests_status
  ON leave_requests(status);

CREATE INDEX idx_leave_requests_employee_dates
  ON leave_requests(employee_id, start_date, end_date);

-- ===== P2-27: schedule_results 唯一约束 =====
-- 清理重复分配记录（保留最新：删除较小 id 的旧记录，保留较大 id 的新记录）
DELETE r1 FROM schedule_results r1
INNER JOIN schedule_results r2
  ON r1.plan_id = r2.plan_id
  AND r1.employee_id = r2.employee_id
  AND r1.work_date = r2.work_date
  AND r1.time_slot = r2.time_slot
  AND r1.id < r2.id;

-- 添加生成列 + 唯一索引
ALTER TABLE schedule_results
  ADD COLUMN assignment_key VARCHAR(255)
  GENERATED ALWAYS AS (CONCAT(plan_id, '|', employee_id, '|', work_date, '|', time_slot)) STORED;

CREATE UNIQUE INDEX ux_schedule_results_key
  ON schedule_results(assignment_key);

-- ===== P2-2/17: 为所有门店生成 9 月日期参数（含中秋节假日）=====
INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve)
SELECT
  s.id,
  d,
  DAYOFWEEK(d),
  CASE
    WHEN d IN ('2026-09-25','2026-09-26') THEN 'HOLIDAY'   -- 中秋法定节假日（周五/周六）
    WHEN d = '2026-09-27' THEN 'WORKDAY'                   -- 调休补班（周日补周五）
    WHEN DAYOFWEEK(d) IN (1,7) THEN 'HOLIDAY'              -- 周末（旧口径，20260818 起改 WEEKEND）
    ELSE 'WORKDAY'
  END,
  CASE WHEN d IN ('2026-09-25','2026-09-26') THEN 1 ELSE 0 END,  -- 法定节假日（09-27 调休补班，非法定节假日）
  CASE WHEN d = '2026-09-24' THEN 1 ELSE 0 END             -- 假日前夕（周四）
FROM stores s
CROSS JOIN (
  SELECT DATE('2026-09-01') + INTERVAL seq DAY AS d
  FROM (
    SELECT 0 seq UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3
    UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7
    UNION ALL SELECT 8 UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11
    UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
    UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19
    UNION ALL SELECT 20 UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23
    UNION ALL SELECT 24 UNION ALL SELECT 25 UNION ALL SELECT 26 UNION ALL SELECT 27
    UNION ALL SELECT 28 UNION ALL SELECT 29
  ) x
) dates
-- 仅插入缺失行，不覆盖已有值（no-op 形式：自赋值）。避免重跑覆盖后续人工/业务修改；
-- 9 月日期口径的最终修正由 20260818_add_weekend_staffing.sql 统一完成。
ON DUPLICATE KEY UPDATE
  week_day = date_parameters.week_day,
  day_type = date_parameters.day_type,
  is_legal_holiday = date_parameters.is_legal_holiday,
  is_holiday_eve = date_parameters.is_holiday_eve;

-- ===== P2-15: 密码修复脚本幂等（只更新占位符）=====
UPDATE users
SET password_hash = '$2b$12$9p9nWbNUcDdJk9wAvqapge0GCFfX4quD8OH6XTJJVn9WPB2xORj12',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'admin'
AND password_hash LIKE '%CHANGE_ME%';

UPDATE users
SET password_hash = '$2b$12$7O.K0j9MXlDUBm1q/8/a6uABHQLinhhc5Jc8jOUZwsLF0yjXbROqq',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'manager'
AND password_hash LIKE '%CHANGE_ME%';
