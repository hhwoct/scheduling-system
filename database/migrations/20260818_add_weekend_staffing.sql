-- ============================================================
-- 20260818: 新增「周末」日期类型（WEEKEND），支持 平日/周末/节假日 三档人数需求
-- 业务口径（与排班算法 RestDayAllocator 的既有假设一致）：
--   平日 WORKDAY = 周一~周四 + 周日
--   周末 WEEKEND = 周五 + 周六（晚市高峰日）
--   节假日 HOLIDAY = 法定节假日（如 2026-09-25/26 中秋）
-- 背景：此前周五/周六/周日都混在 HOLIDAY 里，无法单独配置周末人数需求。
-- 内容：
--   1. 修正中秋法定节假日标记（2026-09-25/26 为 HOLIDAY，09-27 调休补班为 WORKDAY）；
--   2. 周五、周六（非法定节假日）改判为 WEEKEND；
--   3. 周日（曾被历史脚本归入 HOLIDAY 的）改判为 WORKDAY；
--   4. 为每个门店复制当前 HOLIDAY 的人数需求作为 WEEKEND 初始值，后续可在
--      「基础数据 → 人数需求」页面手动调整或上传 Excel 批量设置。
-- 幂等性：依赖唯一键 uk_staffing_req，重复执行不会产生重复数据。
-- 顺序依赖：本脚本须在 20260819_add_ideal_count.sql 之前执行（本脚本回填只更新
--           required_count，由 20260819 添加 ideal_count 列时统一回填）。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260818_add_weekend_staffing.sql
-- ============================================================
USE shift_mvp;

-- 1) 中秋法定节假日标记（2026-09-25 中秋节、09-26 假期第二天；09-27 调休补班按平日）
UPDATE date_parameters
SET is_legal_holiday = 1, day_type = 'HOLIDAY'
WHERE work_date IN ('2026-09-25', '2026-09-26');

-- 2.8 修复：09-27 为调休补班日，不是法定节假日（is_legal_holiday=0）
UPDATE date_parameters
SET is_legal_holiday = 0, day_type = 'WORKDAY'
WHERE work_date = '2026-09-27';

-- 2) 周五、周六（非法定节假日）改判为 WEEKEND。
--    不依赖当前 day_type（旧口径下周五曾被判为 WORKDAY 而非 HOLIDAY，会被遗漏），
--    直接按 is_legal_holiday + DAYOFWEEK 过滤，覆盖所有非节假日的周五/周六。
UPDATE date_parameters
SET day_type = 'WEEKEND'
WHERE is_legal_holiday = 0
  AND DAYOFWEEK(work_date) IN (6, 7);

-- 3) 周日（非法定节假日，曾被历史脚本归入 HOLIDAY 的）改判为 WORKDAY
UPDATE date_parameters
SET day_type = 'WORKDAY'
WHERE day_type = 'HOLIDAY'
  AND is_legal_holiday = 0
  AND DAYOFWEEK(work_date) = 1;

-- 4) 复制 HOLIDAY 人数需求作为 WEEKEND 初始值
-- 幂等：已存在的 WEEKEND 行采用自赋值 no-op，不覆盖后续手工调优的值（仅插入缺失行）
INSERT INTO staffing_requirements (store_id, day_type, workstation_id, time_slot, required_count)
SELECT src.store_id, 'WEEKEND', src.workstation_id, src.time_slot, src.required_count
FROM staffing_requirements AS src
WHERE src.day_type = 'HOLIDAY'
ON DUPLICATE KEY UPDATE
  required_count = staffing_requirements.required_count;
