USE shift_mvp;

-- ============================================================
-- P0-4 修复：数据库无效/共享密码
-- 为 admin、manager 和所有员工账号设置独立真实的 bcrypt 哈希
-- 初始密码：admin=Admin@123456, manager=Manager@123456, 员工=工号
-- ============================================================

START TRANSACTION;

UPDATE users
SET password_hash = CASE username
    WHEN 'admin'   THEN '$2b$12$9p9nWbNUcDdJk9wAvqapge0GCFfX4quD8OH6XTJJVn9WPB2xORj12'
    WHEN 'manager' THEN '$2b$12$7O.K0j9MXlDUBm1q/8/a6uABHQLinhhc5Jc8jOUZwsLF0yjXbROqq'
    WHEN 'E001' THEN '$2b$12$l3EjL7yPaWFybXwKz1waAuwRncJm3/RUEiSJ4uX7OGRO6zm.KDSvK'
    WHEN 'E002' THEN '$2b$12$/D9f2NfOOfkCur1tMElNOOlkgER2TY.BV0EqA3miawYxbjqUz/psy'
    WHEN 'E003' THEN '$2b$12$rXcOCinzcJt8YGVQ99GoPeF5ERAsd9Ar9YZ.qpfW/mkpskifaMmLq'
    WHEN 'E004' THEN '$2b$12$aHPCDo7jdvtjj6XCHrymWe7z8B50LHpN4P5bo.9/05X6RBxqH6gRi'
    WHEN 'E005' THEN '$2b$12$62ZtOfKFWKnEQTMYAJ3AI.5zug9JS/jXi35P77I1E1bFFdWBL/pse'
    WHEN 'E006' THEN '$2b$12$1Cg3k1SbeMTeY2JLoFmjT.cNzm9w5cXXgE3x9WqemvGZikSCZ3Bke'
    WHEN 'E007' THEN '$2b$12$maFdvY9ag5ir544b1uyOoeZhu4976nr0ZRapJDjzOeXWqPL6jxsA6'
    WHEN 'E008' THEN '$2b$12$juONan8SVtofZKKRI4FUcerHubr9L9PNjnpJycz8PgEARtmP9EiWi'
    WHEN 'E009' THEN '$2b$12$WL7T.VZQ9wQ7o34su.0o/uf44ardAdoKsEOt63Zi/ORunRP0eDywO'
    WHEN 'E010' THEN '$2b$12$FQHxMaLxVGTILXfNyy2ZwOrOfmhXHiInniInsN8X0ltOMAZ7jBkY2'
    WHEN 'E011' THEN '$2b$12$vHr3dT9HRWBTZkVSv8ePtuTkWGKq7ApiM67FwZn/pAbnfpEehzxiy'
    WHEN 'E012' THEN '$2b$12$pMc2do5dciHO9jExFonnNeWC7iBAIgzTzxofCzm.2mn1piGxNhaVC'
    WHEN 'E013' THEN '$2b$12$07pHnxboh5F/hWyXiw3rl.9jYJNNEBlFDUqKd9TjzTIzeYJG7jfJq'
    WHEN 'E014' THEN '$2b$12$oDKpXi.fQ7cYZ01hQagEe.oAsAL2vfmGSdhOiXJOeqnU6R9ulq5gS'
    WHEN 'E015' THEN '$2b$12$60p/xa1BMZ8eQJfJKrqdCuHRIFjY8Y.KfJfYcwtiiEKPTVY64ewwy'
    WHEN 'E016' THEN '$2b$12$ijbEdB8x5WQ4E7zjCbjHqOUmdk5IlPa8.9fxEbo.svkrquRUhxzIa'
    WHEN 'E017' THEN '$2b$12$ecKcxo5LnLJCO2tJKjXfkuOPMjByEyVAr6gGiaCH/fnRF9qJw3XeS'
    WHEN 'E018' THEN '$2b$12$wJ389ywE6EL/q5eiM3NOle//Q1.RKPCE6x8LRcqOXRp2p4fOBW83O'
    WHEN 'E019' THEN '$2b$12$CwbqvVKG.LdjwFS9SLNMO.E7cKKK93nA4lkMj8.b4cqTvb/gAC98y'
    WHEN 'E020' THEN '$2b$12$ow5Rb/4R8vw/f0pJm0Jl8OpmW4tZ3azz0HpiHAgdxGt1uMMJspASW'
    WHEN 'E021' THEN '$2b$12$19wSrWatI5NBHvTxkm2Pqu9AF5C5Mc06g1DTGqzidxqGGx6sDm0km'
    WHEN 'E022' THEN '$2b$12$yQlvtUde9z9beaEZdXwUYuNdSdnaNP7fqmB2wlCERFZa8h2RuJ0ya'
    ELSE password_hash
  END,
  updated_at = CURRENT_TIMESTAMP
WHERE username IN ('admin', 'manager', 'E001','E002','E003','E004','E005','E006','E007','E008','E009','E010',
                   'E011','E012','E013','E014','E015','E016','E017','E018','E019','E020','E021','E022');

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
VALUES (1, 1, '系统管理员', 'FIX_PASSWORD_HASHES', 'USER', NULL,
        '为 admin/manager/22名员工设置独立真实 bcrypt 哈希，替代无效/共享占位哈希',
        'P0 安全修复：数据库无效/共享密码');

COMMIT;