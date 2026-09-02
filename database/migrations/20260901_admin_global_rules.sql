-- 2026-09-01 管理端改造配套数据变更(执行环境:MySQL shift_mvp)
-- 说明:以下 SQL 已在开发库手动执行,此文件用于新环境重放与存档。

-- 1) 虚拟门店 id=0:仅承载全局默认规则(status=0,不出现在门店列表)
SET SESSION sql_mode = CONCAT(@@sql_mode, ',NO_AUTO_VALUE_ON_ZERO');
INSERT INTO stores (id, code, name, address, max_employee_count, status, created_at, updated_at)
VALUES (0, 'GLOBAL', '全局默认(虚拟门店,仅承载全局规则)', NULL, 0, 0, NOW(), NOW())
ON DUPLICATE KEY UPDATE updated_at = NOW();

-- 2) 店1 原有规则上移为全局默认;店长修改时自动在各自门店生成覆盖行
INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status, version, created_at, updated_at)
SELECT 0, rule_key, rule_name, rule_value, value_type, remark, status, version, NOW(), NOW()
FROM rule_configs WHERE store_id = 1;
DELETE FROM rule_configs WHERE store_id = 1;

-- 3) 店1 工作站名称去掉「岗」后缀
UPDATE workstations SET name = REPLACE(name, '岗', ''), updated_at = NOW()
WHERE store_id = 1 AND name LIKE '%岗%';

-- 4) 最晚下班统一 04:00:04:00-04:30 时段需求归零
UPDATE staffing_requirements SET required_count = 0, ideal_count = 0, updated_at = NOW()
WHERE store_id = 1 AND time_slot = '04:00:00';

-- 5) 店2 日期参数按店1补齐(节假日/周末日历两店通用)
INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve, created_at)
SELECT 2, d.work_date, d.week_day, d.day_type, d.is_legal_holiday, d.is_holiday_eve, NOW()
FROM date_parameters d
WHERE d.store_id = 1
  AND NOT EXISTS (SELECT 1 FROM date_parameters d2 WHERE d2.store_id = 2 AND d2.work_date = d.work_date);
