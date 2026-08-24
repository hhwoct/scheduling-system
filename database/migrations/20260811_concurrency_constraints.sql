USE shift_mvp;

-- P1-6 并发控制：唯一索引
-- P2-9 数据模型：CHECK 约束
-- P2-12 换班约束

-- 注意：MySQL DDL 隐式提交，不使用事务包装
-- 每个语句独立执行

-- 审查修复（H9）：employee_skills 去重必须先于唯一约束执行。
-- 本文件按文件名序先于 20260811_fix_p2_data_model.sql（其内也有同款去重）执行；
-- 若先加唯一键再去重，存在重复技能行的老库会在下方 ALTER 处报 Duplicate entry
-- 中断整条迁移链，去重逻辑永远执行不到。
-- P2-7: 清理 employee_skills 重复数据（保留最小 id 的记录）
DELETE e1 FROM employee_skills e1
INNER JOIN employee_skills e2
  ON e1.employee_id = e2.employee_id
  AND e1.workstation_id = e2.workstation_id
  AND e1.id > e2.id;

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

-- 不能自己换自己。requester_employee_id / target_employee_id 均为 NOT NULL，
-- 故 <> 恒返回 TRUE/FALSE，不会出现 NULL（NULL 会让 CHECK 判定通过、形同虚设）；
-- 若将来放宽为可空，需改写为
-- CHECK (requester_employee_id IS NULL OR target_employee_id IS NULL OR requester_employee_id <> target_employee_id)。
ALTER TABLE shift_swaps
  ADD CONSTRAINT chk_not_self CHECK (requester_employee_id <> target_employee_id);
