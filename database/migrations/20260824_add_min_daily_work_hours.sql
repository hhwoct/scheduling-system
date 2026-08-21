-- 新增规则：正式员工每日最低工时（上班当天工时不得低于该值）
-- 算法读取 rule_key = min_daily_work_hours（缺省 6.5）；设为 0 表示不限制。
INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status)
SELECT 1, 'min_daily_work_hours', '正式员工每日最低工时', '6.5', 'number', '正式员工上班当天工时不得低于该值（小时），0 表示不限制', 1
WHERE NOT EXISTS (SELECT 1 FROM rule_configs WHERE store_id = 1 AND rule_key = 'min_daily_work_hours');
