USE shift_mvp;

-- 补齐赵保洁(E023)的技能：参照周保洁(E007)，保洁岗主技能 + 服务岗辅助
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT 23, w.id,
  CASE WHEN w.code = 'CLEANING' THEN 5 WHEN w.code = 'SERVICE' THEN 3 ELSE 0 END,
  CASE WHEN w.code = 'CLEANING' THEN 1 ELSE 0 END,
  1
FROM workstations w
WHERE w.store_id = 1 AND w.code IN ('CLEANING','SERVICE');

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'FIX_EMPLOYEE_SKILL', 'EMPLOYEE',
        '补齐赵保洁(E023)保洁岗/服务岗技能，修复连续休息问题');
