USE shift_mvp;

-- 修正：行政岗位（管理/文员仓管/采购/工程/网络）班次 13:00-22:00，
-- 下班点22:00不覆盖22:00及以后时段，因此 22:00 及之后所有槽需求都应为0（原为1导致永久缺人）。
-- 对齐 init_shift_mvp.sql 中 time_slot < '22:00:00' 的边界定义。
UPDATE staffing_requirements r
JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = 0
WHERE w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK')
  AND r.time_slot >= '22:00:00'
  AND r.required_count > 0;

-- 审计按 action_type+remark 去重，重跑不追加重复审计
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
SELECT 1, 1, '系统管理员', 'FIX_STAFFING_REQUIREMENT', 'STAFFING_REQUIREMENT',
       '修正行政岗位22:00及以后需求为0，使需求与13:00-22:00班次下班边界对齐'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM audit_logs a
  WHERE a.action_type = 'FIX_STAFFING_REQUIREMENT'
    AND a.remark = '修正行政岗位22:00及以后需求为0，使需求与13:00-22:00班次下班边界对齐'
);
