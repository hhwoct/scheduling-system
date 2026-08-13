USE shift_mvp;

-- 新增 password_version 列（密码版本号），用于使密码重置后的旧 JWT 失效
ALTER TABLE users
  ADD COLUMN password_version INT NOT NULL DEFAULT 1 AFTER status;

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
VALUES (1, 1, '系统管理员', 'ADD_PASSWORD_VERSION', 'USER', NULL,
        '新增 password_version 列，密码重置时递增使旧令牌失效',
        'P0 安全加固：密码重置后令牌失效');
