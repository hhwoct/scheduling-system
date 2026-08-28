-- ============================================================
-- 20261002: 提高 WEEKEND / HOLIDAY 档晚市高峰人数需求（周五/周六明显多于平日）
--
-- 背景：生成排班后周五/周六（WEEKEND 档）在岗人数与平日差距不明显——
--       三档 20:00 合计需求 WORKDAY=15、WEEKEND=17、HOLIDAY=17，
--       多数工作站 WEEKEND 与 WORKDAY 数值相同（仅内吧/外吧多 1）。
-- 目标：周五/周六晚市高峰（20:00 前后）合计需求 ≈ 22 人（业务口径：约 22 人），
--       HOLIDAY（节假日）保持 ≥ WEEKEND，WORKDAY 不变。
--       调整后 20:00 合计：WORKDAY=15 → WEEKEND=22 → HOLIDAY=23。
--
-- 调整范围（仅晚市高峰段 18:30~23:30，凌晨/白天时段不动）：
--   WEEKEND：厨房/传送/服务 20:00-21:30 2→3、18:30-19:30/22:00-22:30 1→2；
--            咨客 18:30-19:30/22:00-23:30 1→2；客户经理 20:00-21:30 1→2；
--            内吧/外吧 18:30-19:30/22:00-23:30 1→2；保洁 20:00-23:30 1→2；
--            ideal_count 同步 +1（软性填充目标）。
--   HOLIDAY：同 WEEKEND 且 20:00-21:30 主要岗位保持/达到 3（节假日最高）。
--
-- 幂等/安全：CASE 仅命中本次要改的时段，未命中时段 ELSE 保留现值；
--            若某格已被人工调整为其他值，CASE 命中分支仍会写入目标值——
--            本脚本即业务口径（演示配置），重复执行结果一致。
-- ============================================================
USE shift_mvp;

-- ============ WEEKEND（周五/周六晚市高峰日）============

-- 厨房：18:30-19:30 → 2，20:00-21:30 → 3，22:00-22:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 3
      WHEN r.time_slot IN ('18:30:00','19:00:00','19:30:00','22:00:00','22:30:00') THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 4
      WHEN r.time_slot IN ('18:30:00','19:00:00','19:30:00','22:00:00','22:30:00') THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'KITCHEN'
  AND r.time_slot BETWEEN '18:30:00' AND '22:30:00';

-- 传送：18:30-19:30 → 2，20:00-21:30 → 3，22:00-23:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'DELIVERY'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 服务：同传送
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'SERVICE'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 咨客：18:30-19:30 → 2，22:00-23:30 → 2（20:00-21:30 保持 2；ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'RECEPTION'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 客户经理：20:00-21:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'CUSTOMER_MANAGER'
  AND r.time_slot BETWEEN '20:00:00' AND '21:30:00';

-- 内吧：18:30-19:30 → 2，22:00-23:30 → 2（20:00-21:30 保持 2；ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'INNER_BAR'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 外吧：同内吧
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'OUTER_BAR'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 保洁：20:00-23:30 → 2（20:00-21:30 ideal +1，22:00-23:30 ideal 2→3）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 2
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'WEEKEND' AND w.code = 'CLEANING'
  AND r.time_slot BETWEEN '20:00:00' AND '23:30:00';

-- ============ HOLIDAY（节假日，保持 ≥ WEEKEND）============

-- 厨房：18:30-19:30 → 2，20:00-21:30 → 3，22:00-23:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'KITCHEN'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 传送：18:30-19:30 → 2，20:00-21:30 → 3（20:00 起补足到 3），22:00-23:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'DELIVERY'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 服务：同传送
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'SERVICE'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 咨客：18:30-19:30 → 2，20:00-21:30 → 3，22:00-23:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 3
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 4
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 3
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'RECEPTION'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 客户经理：20:00-21:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot IN ('20:00:00','20:30:00','21:00:00','21:30:00') THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'CUSTOMER_MANAGER'
  AND r.time_slot BETWEEN '20:00:00' AND '21:30:00';

-- 内吧：18:30-19:30 → 2，22:00-23:30 → 2（ideal +1）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'INNER_BAR'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 外吧：同内吧
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '18:30:00' AND '19:30:00' THEN 2
      WHEN r.time_slot BETWEEN '22:00:00' AND '23:30:00' THEN 2
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'OUTER_BAR'
  AND r.time_slot BETWEEN '18:30:00' AND '23:30:00';

-- 保洁：20:00-21:30 → 2（ideal +1），22:00-23:30 保持 2（ideal 2→3）
UPDATE staffing_requirements r JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '21:30:00' THEN 2
      ELSE r.required_count END,
    r.ideal_count = CASE
      WHEN r.time_slot BETWEEN '20:00:00' AND '23:30:00' THEN 3
      ELSE r.ideal_count END
WHERE r.store_id = 1 AND r.day_type = 'HOLIDAY' AND w.code = 'CLEANING'
  AND r.time_slot BETWEEN '20:00:00' AND '23:30:00';
