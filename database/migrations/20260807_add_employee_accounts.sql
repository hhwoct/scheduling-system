-- 为 22 名员工创建员工登录账号
-- 用户名 = 工号（E001...E022），角色 EMPLOYEE。
-- ⚠️ 注意（审查修复 M5 口径说明）：本迁移所有员工账号共用同一个开发期占位哈希
-- （即 bcrypt("admin123")，与当时 admin/manager 相同），并非注释历史所称"密码=工号"；
-- 后续 20260811_fix_password_hashes.sql 会为每个账号替换为工号对应的独立哈希。
-- 若迁移链在 20260811 之前中断，全部账号密码为 admin123，请勿按"密码=工号"排查。
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
