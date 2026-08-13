USE shift_mvp;

-- 员工表添加兼职标记
ALTER TABLE employees
  ADD COLUMN is_parttime TINYINT NOT NULL DEFAULT 0 COMMENT '是否兼职人员（仅可安排低技术含量岗位）' AFTER max_weekly_hours;

-- 插入 10 名兼职人员（工号 E101~E110，部门=兼职，is_parttime=1）
-- 兼职只具备低技能岗位（保洁/咨客/传送/服务）技能，见下方 employee_skills 插入
INSERT INTO employees (store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, is_parttime, status) VALUES
(1, 'E101', '兼保洁A', '13900000101', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E102', '兼保洁B', '13900000102', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E103', '兼咨客A', '13900000103', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E104', '兼咨客B', '13900000104', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E105', '兼传送A', '13900000105', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E106', '兼传送B', '13900000106', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E107', '兼服务A', '13900000107', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E108', '兼服务B', '13900000108', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E109', '兼服务C', '13900000109', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E110', '兼服务D', '13900000110', '兼职', '2026-08-01', '服务岗', 32, 1, 1);

-- 兼职技能：仅低技术含量岗位（保洁/咨客/传送/服务），算法据此只能安排兼职到这些岗位
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id,
  CASE
    WHEN e.primary_position = w.name THEN 4
    ELSE 2
  END AS skill_score,
  CASE WHEN e.primary_position = w.name THEN 1 ELSE 0 END AS is_primary_skill,
  1
FROM employees e
CROSS JOIN workstations w
WHERE e.store_id = 1 AND w.store_id = 1
  AND e.employee_no LIKE 'E1%'
  AND w.code IN ('CLEANING', 'RECEPTION', 'DELIVERY', 'SERVICE');

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'ADD_PARTTIME_EMPLOYEES', 'EMPLOYEE',
        '新增 10 名兼职人员（E101~E110），仅具备低技能岗位（保洁/咨客/传送/服务）技能，is_parttime=1');
