USE shift_mvp;

-- P1-6 并发控制：唯一索引
-- P2-9 数据模型：CHECK 约束
-- P2-12 换班约束

-- 注意：MySQL DDL 隐式提交，不使用事务包装
-- 每个语句独立执行

-- 员工技能唯一
ALTER TABLE employee_skills
  ADD CONSTRAINT ux_employee_skills_employee_workstation UNIQUE (employee_id, workstation_id);

-- 排班计划名唯一（门店内）
ALTER TABLE schedule_plans
  ADD CONSTRAINT ux_schedule_plans_store_name UNIQUE (store_id, plan_name);

-- 休息日标记
ALTER TABLE schedule_summaries
  ADD CONSTRAINT chk_is_rest_day CHECK (is_rest_day IN (0, 1));

-- 跨天班次标记
ALTER TABLE shift_templates
  ADD CONSTRAINT chk_is_cross_day CHECK (is_cross_day IN (0, 1));

-- 换班状态
ALTER TABLE shift_swaps
  ADD CONSTRAINT chk_shift_swap_status CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED'));

-- 请假类型
ALTER TABLE leave_requests
  ADD CONSTRAINT chk_leave_type CHECK (leave_type IN ('PERSONAL', 'SICK', 'ANNUAL'));

-- 不能自己换自己
ALTER TABLE shift_swaps
  ADD CONSTRAINT chk_not_self CHECK (requester_employee_id <> target_employee_id);
