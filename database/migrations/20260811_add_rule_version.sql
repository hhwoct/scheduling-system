USE shift_mvp;

-- P1-10 乐观锁：rule_configs 表添加 version 列
ALTER TABLE rule_configs
  ADD COLUMN version INT NOT NULL DEFAULT 1 AFTER status;

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
VALUES (1, 1, '系统管理员', 'ADD_RULE_VERSION', 'RULE_CONFIG', NULL,
        'rule_configs 表新增 version 列（乐观锁）',
        'P1 并发控制：规则配置乐观锁');
