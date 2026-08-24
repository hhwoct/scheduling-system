-- ============================================================
-- 20260825: 新增「店长」专属工作站，张店长(E001)专职该岗
--
-- 背景：店长此前按「管理岗」排班——管理岗唯一班次 S1 为 13:00-22:00，
--       且历史数据残留管理岗凌晨 00:00-02:00 / 深夜 22:00-23:30 需求=1
--       （与 20260819 声明口径「行政岗 13:00-22:00」矛盾），
--       导致店长被排 13:00 上班 + 凌晨/深夜残差补班，单日工时达 13.5h。
--
-- 内容：
--   1. 新增工作站「店长」(code=BOSS, sort_order=0 排最前)；
--   2. 新增班次模板「店长班」S10（19:00-次日04:00 跨天，9 小时）；
--   3. S10 映射到「店长」工作站；
--   4. 店长站人数需求：19:00-23:30 + 00:00-03:30 = 1（三种 day_type；
--      凌晨时段按营业日口径归属前一营业日，算法自行取档）；
--   5. 张店长(E001)：主岗位改为「店长」，技能重置为仅「店长」站 5 分主技能
--      （移除管理岗/楼面辅助技能，避免再被拉去顶班）；
--   6. 管理岗/文员仓管/采购/工程/网络 凌晨(<06:00)与 22:00 起需求清零，
--      对齐 20260819 迁移声明的「行政岗 13:00-22:00」口径（幂等条件更新）。
--
-- 幂等性：全部语句带 NOT EXISTS / ON DUPLICATE / 条件 UPDATE 守卫，
--         对「新装库（init 已吸收 20260824）」与「老库升级」两种路径均可安全执行。
-- 注意：第 5 步会覆盖 E001 在员工管理里手工调整过的技能，属有意为之（专职化）。
-- ============================================================
USE shift_mvp;

-- 1) 新增「店长」工作站
INSERT INTO workstations (store_id, code, name, sort_order, remark, status)
SELECT 1, 'BOSS', '店长', 0, '门店店长专属岗位', 1
WHERE NOT EXISTS (SELECT 1 FROM workstations WHERE store_id = 1 AND code = 'BOSS');

-- 2) 新增「店长班」班次模板 S10（19:00-次日04:00 跨天）
INSERT INTO shift_templates (store_id, code, name, start_time, end_time, is_cross_day, priority, status)
SELECT 1, 'S10', '店长班', '19:00:00', '04:00:00', 1, 10, 1
WHERE NOT EXISTS (SELECT 1 FROM shift_templates WHERE store_id = 1 AND code = 'S10');

-- 3) S10 映射到「店长」工作站（自然键，不硬编码 id）
INSERT INTO shift_workstations (shift_template_id, workstation_id)
SELECT t.id, w.id FROM shift_templates t
JOIN workstations w ON w.store_id = t.store_id AND w.code = 'BOSS'
WHERE t.store_id = 1 AND t.code = 'S10'
  AND NOT EXISTS (SELECT 1 FROM shift_workstations sw
                  WHERE sw.shift_template_id = t.id AND sw.workstation_id = w.id);

-- 4) 店长站人数需求：19:00-23:30 + 00:00-03:30 = 1（三种 day_type）
--    审查修复（P2）：ON DUPLICATE 用自赋值 no-op（照 20260818 迁移风格），
--    不覆盖用户在「人数需求」页手工调整过的值；仅插入缺失行。
INSERT INTO staffing_requirements (store_id, day_type, workstation_id, time_slot, required_count)
SELECT w.store_id, dt.day_type, w.id, ts.time_slot, 1
FROM workstations w
CROSS JOIN (SELECT 'WORKDAY' AS day_type UNION ALL SELECT 'WEEKEND' UNION ALL SELECT 'HOLIDAY') dt
CROSS JOIN (
  SELECT '19:00:00' AS time_slot UNION ALL SELECT '19:30:00' UNION ALL SELECT '20:00:00'
  UNION ALL SELECT '20:30:00' UNION ALL SELECT '21:00:00' UNION ALL SELECT '21:30:00'
  UNION ALL SELECT '22:00:00' UNION ALL SELECT '22:30:00' UNION ALL SELECT '23:00:00'
  UNION ALL SELECT '23:30:00' UNION ALL SELECT '00:00:00' UNION ALL SELECT '00:30:00'
  UNION ALL SELECT '01:00:00' UNION ALL SELECT '01:30:00' UNION ALL SELECT '02:00:00'
  UNION ALL SELECT '02:30:00' UNION ALL SELECT '03:00:00' UNION ALL SELECT '03:30:00'
) ts
WHERE w.store_id = 1 AND w.code = 'BOSS'
ON DUPLICATE KEY UPDATE required_count = staffing_requirements.required_count;

-- 5) 张店长(E001)专职「店长」岗：主岗位 + 技能重置
UPDATE employees
SET primary_position = '店长', updated_at = CURRENT_TIMESTAMP
WHERE store_id = 1 AND employee_no = 'E001' AND primary_position <> '店长';

DELETE FROM employee_skills
WHERE employee_id = (SELECT id FROM employees WHERE store_id = 1 AND employee_no = 'E001');

INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id, 5, 1, 1
FROM employees e
JOIN workstations w ON w.store_id = e.store_id AND w.code = 'BOSS'
WHERE e.store_id = 1 AND e.employee_no = 'E001'
ON DUPLICATE KEY UPDATE skill_score = 5, is_primary_skill = 1, status = 1;

-- 6) 行政岗凌晨/深夜需求清零（对齐「行政岗 13:00-22:00」口径；幂等条件更新）
UPDATE staffing_requirements r
JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = 0, r.ideal_count = 0
WHERE w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK')
  AND (r.time_slot >= '22:00:00' OR r.time_slot < '06:00:00')
  AND (r.required_count > 0 OR r.ideal_count > 0);

-- 审计留痕（按 action_type+remark 去重，重跑不重复追加）
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
SELECT 1, 1, '系统管理员', 'ADD_BOSS_WORKSTATION', 'WORKSTATION',
       '新增店长工作站(BOSS)+店长班(S10 19:00-04:00)，张店长(E001)专职化，行政岗凌晨/深夜需求清零'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM audit_logs a
  WHERE a.action_type = 'ADD_BOSS_WORKSTATION'
    AND a.remark LIKE '新增店长工作站%'
);
