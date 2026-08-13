USE shift_mvp;

-- 低技术含量岗位标记：该岗位缺口可建议找兼职人员临时填补
-- 方案B确认清单：传送岗(DELIVERY)、保洁岗(CLEANING)、服务岗(SERVICE)、咨客岗(RECEPTION)
ALTER TABLE workstations
  ADD COLUMN is_low_skill TINYINT NOT NULL DEFAULT 0 COMMENT '是否低技术含量岗位（缺口可兼职填补）' AFTER sort_order;

-- 初始化低技能岗位标记
UPDATE workstations SET is_low_skill = 1 WHERE code IN ('DELIVERY', 'CLEANING', 'SERVICE', 'RECEPTION');

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
VALUES (1, 1, '系统管理员', 'ADD_LOW_SKILL_FLAG', 'WORKSTATION', NULL,
        '为传送/保洁/服务/咨客岗添加低技术含量标记，缺口可建议兼职填补',
        '低技能岗位标记：缺口显示绿色+兼职建议');