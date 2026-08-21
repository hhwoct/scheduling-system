-- 新增"保洁班"S9（16:00-次日04:00，覆盖保洁岗）：
-- 让全职保洁员工（保洁技能 5 分）能排上完整班次，落实"全职优先、兼职填充剩余"原则。
INSERT INTO shift_templates (store_id, code, name, start_time, end_time, is_cross_day, priority, status)
SELECT 1, 'S9', '保洁班', '16:00:00', '04:00:00', 1, 9, 1
WHERE NOT EXISTS (SELECT 1 FROM shift_templates WHERE store_id = 1 AND code = 'S9');

INSERT INTO shift_workstations (shift_template_id, workstation_id)
SELECT t.id, 6 FROM shift_templates t
WHERE t.store_id = 1 AND t.code = 'S9'
  AND NOT EXISTS (SELECT 1 FROM shift_workstations sw WHERE sw.shift_template_id = t.id AND sw.workstation_id = 6);
