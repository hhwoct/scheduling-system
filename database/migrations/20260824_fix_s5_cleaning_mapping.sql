-- 楼面B班(S5)不再覆盖保洁岗：保洁需求由专属"保洁班"(S9)承接，
-- 否则楼面员工会在 S9 之前通过 S5 消耗保洁需求，导致全职保洁排不上班。
DELETE sw FROM shift_workstations sw
JOIN shift_templates t ON t.id = sw.shift_template_id
WHERE t.store_id = 1 AND t.code = 'S5' AND sw.workstation_id = 6;
