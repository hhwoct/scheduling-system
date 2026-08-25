USE shift_mvp;

-- 审查修复（M1）：老库升级路径补建赵保洁(E023)员工档案。
-- 旧库（20260806 前备份）仅有 E001~E022，而新装库含 E023，两路径数据漂移；
-- 下方技能补全与 20260811_fix_password_hashes 的 E023 账号兜底均依赖该员工存在。
-- NOT EXISTS 守卫保证幂等（新装库 init 已含 E023，重复执行无副作用）。
INSERT INTO employees (store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, status)
SELECT 1, 'E023', '赵保洁', '12312341234', '保洁', '2024-01-23', '保洁岗', 48, 1
WHERE NOT EXISTS (SELECT 1 FROM employees WHERE store_id = 1 AND employee_no = 'E023');

-- 补齐赵保洁(E023)的技能：参照周保洁(E007)，保洁岗主技能 + 服务岗辅助
-- 从自然键派生（employee_no / workstations.code），不硬编码 store_id=1 / employee_id=23；
-- ON DUPLICATE KEY UPDATE 使其幂等且可自愈（重跑不会因 uk_employee_workstation 报重复键）。
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id,
  CASE WHEN w.code = 'CLEANING' THEN 5 WHEN w.code = 'SERVICE' THEN 3 ELSE 0 END,
  CASE WHEN w.code = 'CLEANING' THEN 1 ELSE 0 END,
  1
FROM employees e
JOIN workstations w ON w.store_id = e.store_id AND w.code IN ('CLEANING','SERVICE')
WHERE e.employee_no = 'E023' AND e.status = 1
ON DUPLICATE KEY UPDATE
  skill_score = VALUES(skill_score),
  is_primary_skill = VALUES(is_primary_skill),
  status = VALUES(status);

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'FIX_EMPLOYEE_SKILL', 'EMPLOYEE',
        '补齐赵保洁(E023)保洁岗/服务岗技能，修复连续休息问题');
