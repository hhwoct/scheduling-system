-- ============================================================
-- 20260825: 管理岗技能补强——E006 孙采购 管理岗 3 分 → 4 分
--
-- 背景：店长(E001)专职「店长」站后，管理岗 13:00-21:30 需求
--       仅由行政员工 3 分辅助技能承接，副手顶店长班日叠加时
--       管理岗缺口 27 时段/月。E006 采购岗 5 分主技能不受影响，
--       将其管理岗辅助技能提升至 4 分，管理岗分配优先级高于
--       E002/E003（3 分），缺口应显著下降。
--
-- 幂等性：ON DUPLICATE 固定写入 4 分；两种安装路径均可安全执行。
-- ============================================================
USE shift_mvp;

INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id, 4, 0, 1
FROM employees e
JOIN workstations w ON w.store_id = e.store_id AND w.code = 'MANAGER'
WHERE e.store_id = 1 AND e.employee_no = 'E006'
ON DUPLICATE KEY UPDATE skill_score = 4, is_primary_skill = 0, status = 1;

-- 审计留痕（幂等）
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
SELECT 1, 1, '系统管理员', 'UPGRADE_ADMIN_SKILL', 'EMPLOYEE',
       'E006 管理岗技能 3→4 分（店长专职后补强管理岗承接）'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM audit_logs a
  WHERE a.action_type = 'UPGRADE_ADMIN_SKILL'
);
