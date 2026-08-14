USE shift_mvp;

-- 修正：行政岗位（管理/文员仓管/采购/工程/网络）班次 13:00-22:00，
-- 下班点22:00不覆盖22:00时段，因此22:00槽需求应为0（原为1导致永久缺人）。
-- 对齐 init_shift_mvp.sql 中 time_slot < '22:00:00' 的边界定义。
UPDATE staffing_requirements r
JOIN workstations w ON w.id = r.workstation_id
SET r.required_count = 0
WHERE w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK')
  AND r.time_slot = '22:00:00'
  AND r.required_count > 0;

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'FIX_STAFFING_REQUIREMENT', 'STAFFING_REQUIREMENT',
        '修正行政岗位22:00需求为0，使需求与13:00-22:00班次下班边界对齐');
