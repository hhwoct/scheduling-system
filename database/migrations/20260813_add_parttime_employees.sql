USE shift_mvp;

-- 员工表添加兼职标记
ALTER TABLE employees
  ADD COLUMN is_parttime TINYINT NOT NULL DEFAULT 0 COMMENT '是否兼职人员（仅可安排低技术含量岗位）' AFTER max_weekly_hours;

-- 插入 10 名兼职人员（工号 E101~E110，部门=兼职，is_parttime=1）
-- 兼职只具备低技能岗位（保洁/咨客/传送/服务）技能，见下方 employee_skills 插入
INSERT INTO employees (store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, is_parttime, status) VALUES
(1, 'E101', '兼保洁A', '12312341234', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E102', '兼保洁B', '12312341234', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E103', '兼咨客A', '12312341234', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E104', '兼咨客B', '12312341234', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E105', '兼传送A', '12312341234', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E106', '兼传送B', '12312341234', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E107', '兼服务A', '12312341234', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E108', '兼服务B', '12312341234', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E109', '兼服务C', '12312341234', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E110', '兼服务D', '12312341234', '兼职', '2026-08-01', '服务岗', 32, 1, 1);

-- 兼职技能：仅低技术含量岗位（保洁/咨客/传送/服务），算法据此只能安排兼职到这些岗位
-- 用 e.is_parttime=1 而非 LIKE 'E1%'（过宽，可能误纳其它 E1xx 工号）；
-- 岗位名→code 显式映射（避免依赖 primary_position 与 workstations.name 的字符串精确匹配易漂移）。
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id,
  CASE WHEN m.primary_position IS NULL THEN 2 ELSE 4 END AS skill_score,
  CASE WHEN m.primary_position IS NULL THEN 0 ELSE 1 END AS is_primary_skill,
  1
FROM employees e
CROSS JOIN workstations w
LEFT JOIN (
  SELECT '保洁岗' AS primary_position, 'CLEANING' AS code UNION ALL
  SELECT '咨客岗', 'RECEPTION' UNION ALL
  SELECT '传送岗', 'DELIVERY' UNION ALL
  SELECT '服务岗', 'SERVICE'
) m ON m.primary_position = e.primary_position AND m.code = w.code
WHERE e.store_id = 1 AND w.store_id = 1
  AND e.is_parttime = 1
  AND w.code IN ('CLEANING', 'RECEPTION', 'DELIVERY', 'SERVICE');

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'ADD_PARTTIME_EMPLOYEES', 'EMPLOYEE',
        '新增 10 名兼职人员（E101~E110），仅具备低技能岗位（保洁/咨客/传送/服务）技能，is_parttime=1');
