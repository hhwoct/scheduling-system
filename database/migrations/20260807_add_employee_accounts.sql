-- 为 22 名员工创建员工登录账号
-- 用户名 = 工号（E001...E022），密码 = 工号（如 E001），角色 EMPLOYEE，关联对应 employee_id
USE shift_mvp;

INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
SELECT
  e.store_id,
  e.employee_no,
  '$2y$12$ZBREG25aSw/EznOB14dL8Oj9kFOrFJSSVpmyrRm2q9qGAplc1mqC6',  -- 与管理员同哈希（开发环境统一占位）
  e.name,
  'EMPLOYEE',
  1,
  NOW(),
  NOW()
FROM employees e
WHERE e.status = 1
  AND NOT EXISTS (SELECT 1 FROM users u WHERE u.username = e.employee_no);
