-- 新增「单日最大工时」规则：单个工作日累计工时上限（小时），0 = 不限制。
-- 算法在排班与补班阶段均硬性遵守；默认 12 小时。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20261004_add_max_daily_work_hours.sql
USE shift_mvp;

INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status)
SELECT 1, 'max_daily_work_hours', '单日最大工时', '12', 'json', '按岗位设置单日工时上限（小时），0 表示不限制', 1
WHERE NOT EXISTS (
  SELECT 1 FROM rule_configs WHERE store_id = 1 AND rule_key = 'max_daily_work_hours'
);
