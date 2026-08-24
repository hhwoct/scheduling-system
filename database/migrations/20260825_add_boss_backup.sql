-- ============================================================
-- 20260825: 店长班副手配置——E002 李文员、E003 王仓管兼店长站技能
--
-- 背景：店长站需求每天 1 人，但仅店长(E001)有该技能，
--       店长休息日店长班无人可排（缺口）；且单副手时其顶班日
--       会被凌晨收尾/午后管理岗等残差补班压到 16 小时。
--       给行政员工 E002/E003 配店长站 4 分辅助技能
--       （店长 5 分主技能保持优先），店长休息时由副手轮替顶班。
--
-- 内容：
--   1. E002/E003 增加店长站(BOSS)技能：4 分、非主技能（店长 5 分优先，副手兜底）；
--   2. S10 班次优先级 10→1：店长班先于 S1 等班次分配，
--      保证店长休息日副手先被店长班占用（否则副手会被 13:00 行政班先占走，
--      店长班仍无人可排）。
--
-- 幂等性：ON DUPLICATE / 条件 UPDATE / NOT EXISTS 守卫，两种安装路径均可安全执行。
-- ============================================================
USE shift_mvp;

-- 1) E002 配店长站 4 分辅助技能
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id, 4, 0, 1
FROM employees e
JOIN workstations w ON w.store_id = e.store_id AND w.code = 'BOSS'
WHERE e.store_id = 1 AND e.employee_no = 'E002'
ON DUPLICATE KEY UPDATE skill_score = 4, is_primary_skill = 0, status = 1;

-- 1b) E003 配店长站 4 分辅助技能（第二副手，三班轮转）
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id, 4, 0, 1
FROM employees e
JOIN workstations w ON w.store_id = e.store_id AND w.code = 'BOSS'
WHERE e.store_id = 1 AND e.employee_no = 'E003'
ON DUPLICATE KEY UPDATE skill_score = 4, is_primary_skill = 0, status = 1;

-- 2) S10 优先级 10 → 1（店长班优先分配）
UPDATE shift_templates
SET priority = 1, updated_at = CURRENT_TIMESTAMP
WHERE store_id = 1 AND code = 'S10' AND priority <> 1;

-- 审计留痕（幂等）
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
SELECT 1, 1, '系统管理员', 'ADD_BOSS_BACKUP', 'EMPLOYEE',
       'E002/E003 配置店长站4分辅助技能（店长休息日轮替顶班），S10 优先级提升至1'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM audit_logs a
  WHERE a.action_type = 'ADD_BOSS_BACKUP'
);
