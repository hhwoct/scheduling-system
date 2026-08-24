USE shift_mvp;

START TRANSACTION;

-- 审查修复（M3）：仅当当前哈希为无效/占位格式（非合法 bcrypt）时才替换，
-- 防止脚本重跑时把已改好的密码覆盖回初始值（重跑幂等）。
UPDATE users
SET password_hash = '$2y$12$ZBREG25aSw/EznOB14dL8Oj9kFOrFJSSVpmyrRm2q9qGAplc1mqC6',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'admin'
  AND (password_hash IS NULL OR LENGTH(password_hash) <> 60 OR password_hash NOT LIKE '$2%');

UPDATE users
SET password_hash = '$2y$12$ZBREG25aSw/EznOB14dL8Oj9kFOrFJSSVpmyrRm2q9qGAplc1mqC6',
    updated_at = CURRENT_TIMESTAMP
WHERE username = 'manager'
  AND (password_hash IS NULL OR LENGTH(password_hash) <> 60 OR password_hash NOT LIKE '$2%');

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