USE shift_mvp;

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
