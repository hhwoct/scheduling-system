USE shift_mvp;

START TRANSACTION;

UPDATE users
SET password_hash = '$2y$12$ZBREG25aSw/EznOB14dL8Oj9kFOrFJSSVpmyrRm2q9qGAplc1mqC6',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'admin';

UPDATE users
SET password_hash = '$2y$12$ZBREG25aSw/EznOB14dL8Oj9kFOrFJSSVpmyrRm2q9qGAplc1mqC6',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'manager';

INSERT INTO audit_logs (
  store_id,
  operator_user_id,
  operator_name,
  action_type,
  target_type,
  target_id,
  after_content,
  remark
)
VALUES (
  1,
  1,
  '系统管理员',
  'HARDEN_PASSWORDS',
  'USER',
  NULL,
  '将初始化管理员账号密码迁移为 BCrypt 哈希（work factor 12）',
  '认证安全加固迁移'
);

COMMIT;