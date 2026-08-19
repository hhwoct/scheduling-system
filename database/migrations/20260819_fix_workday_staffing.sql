-- ============================================================
-- 20260819: 回填 WORKDAY（平日）人数需求
-- 背景：8-6 初始化时旧版 init_shift_mvp.sql 未配置 WORKDAY 需求
--       （仅 HOLIDAY 含周末/节假日），20260818 迁移拆分 WEEKEND 时
--       只复制了 HOLIDAY→WEEKEND，WORKDAY 仍为全 0，
--       导致周一~周四+周日按需求排班时无任何岗位安排。
-- 内容：按当前 init_shift_mvp.sql 的业务口径重算 WORKDAY 全部时段：
--       行政岗(管理/文员仓管/采购/工程/网络) 13:00-22:00 = 1
--       厨房 18:00-23:30 = 2，00:00-03:00 = 1
--       服务/传送 19:00-23:30 = 2，00:00-04:00 = 1
--       咨客/客户经理 19:00-23:30 = 1
--       内吧/外吧 18:30-23:30 = 1，00:00-04:00 = 1
--       保洁 21:00-23:30 或 00:00-06:00 = 1
--       其余时段 = 0（06:00-11:30 闭店）
-- 幂等性：UPDATE 无条件按口径重算 WORKDAY 行，重复执行结果一致。
-- 执行方式：mysql -u root -p shift_mvp < database/migrations/20260819_fix_workday_staffing.sql
-- ============================================================
USE shift_mvp;

UPDATE staffing_requirements r
JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
    WHEN w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK')
         AND r.time_slot >= '13:00:00' AND r.time_slot < '22:00:00' THEN 1
    WHEN w.code = 'KITCHEN' AND r.time_slot >= '18:00:00' AND r.time_slot <= '23:30:00' THEN 2
    WHEN w.code = 'KITCHEN' AND (r.time_slot >= '00:00:00' AND r.time_slot < '03:00:00') THEN 1
    WHEN w.code IN ('SERVICE','DELIVERY') AND (r.time_slot >= '19:00:00' AND r.time_slot <= '23:30:00') THEN 2
    WHEN w.code IN ('SERVICE','DELIVERY') AND (r.time_slot >= '00:00:00' AND r.time_slot < '04:00:00') THEN 1
    WHEN w.code IN ('RECEPTION','CUSTOMER_MANAGER') AND (r.time_slot >= '19:00:00' AND r.time_slot <= '23:30:00') THEN 1
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (r.time_slot >= '18:30:00' AND r.time_slot <= '23:30:00') THEN 1
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (r.time_slot >= '00:00:00' AND r.time_slot < '04:00:00') THEN 1
    WHEN w.code = 'CLEANING' AND (r.time_slot >= '21:00:00' AND r.time_slot <= '23:30:00'
        OR r.time_slot >= '00:00:00' AND r.time_slot < '06:00:00') THEN 1
    ELSE 0
  END,
  r.ideal_count = CASE
    WHEN w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK')
         AND r.time_slot >= '13:00:00' AND r.time_slot < '22:00:00' THEN 1
    WHEN w.code = 'KITCHEN' AND r.time_slot >= '18:00:00' AND r.time_slot <= '23:30:00' THEN 2
    WHEN w.code = 'KITCHEN' AND (r.time_slot >= '00:00:00' AND r.time_slot < '03:00:00') THEN 1
    WHEN w.code IN ('SERVICE','DELIVERY') AND (r.time_slot >= '19:00:00' AND r.time_slot <= '23:30:00') THEN 2
    WHEN w.code IN ('SERVICE','DELIVERY') AND (r.time_slot >= '00:00:00' AND r.time_slot < '04:00:00') THEN 1
    WHEN w.code IN ('RECEPTION','CUSTOMER_MANAGER') AND (r.time_slot >= '19:00:00' AND r.time_slot <= '23:30:00') THEN 1
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (r.time_slot >= '18:30:00' AND r.time_slot <= '23:30:00') THEN 1
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (r.time_slot >= '00:00:00' AND r.time_slot < '04:00:00') THEN 1
    WHEN w.code = 'CLEANING' AND (r.time_slot >= '21:00:00' AND r.time_slot <= '23:30:00'
        OR r.time_slot >= '00:00:00' AND r.time_slot < '06:00:00') THEN 1
    ELSE 0
  END
WHERE r.day_type = 'WORKDAY';

-- 审计留痕（按 action_type+remark 去重，重跑不重复追加）
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
SELECT 1, 1, '系统管理员', 'FIX_STAFFING_REQUIREMENT', 'STAFFING_REQUIREMENT',
       '回填 WORKDAY 平日人数需求（按 init 业务口径：行政13-22点1人/厨房2人/服务传送2人/吧台1人/保洁1人）'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM audit_logs a
  WHERE a.action_type = 'FIX_STAFFING_REQUIREMENT'
    AND a.remark LIKE '回填 WORKDAY 平日人数需求%'
);
