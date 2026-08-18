USE shift_mvp;

-- ============================================================
-- P0-4 修复：数据库无效/共享密码
-- 为 admin、manager 和所有员工账号（E001~E023）设置独立真实的 bcrypt 哈希
-- 初始密码：admin=Admin@123456, manager=Manager@123456, 员工=工号（如 E001）
--
-- ⚠️ 安全提示：以下为「已知明文」的一次性初始密码，仅用于修复历史无效/共享哈希，
--            使账号可重新登录。部署后应尽快改密/轮换。当前应用提供「忘记密码」
--            流程（/auth/send-reset-otp + /auth/forgot-password，凭用户名+手机号+OTP
--            重置），但尚无「登录后强制改密」接口，请按需在应用层补齐。
-- ============================================================

DELIMITER $$
DROP PROCEDURE IF EXISTS tmp_fix_password_hashes$$
CREATE PROCEDURE tmp_fix_password_hashes()
BEGIN
    DECLARE v_rows BIGINT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

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
        WHEN 'E023' THEN '$2b$12$Y/M96/9xESm86gGkSP10mOeZjeYiGasg9w2WrtrnHV9qd7iVFFE52'
        ELSE password_hash
      END,
      updated_at = CURRENT_TIMESTAMP
    WHERE username IN ('admin', 'manager', 'E001','E002','E003','E004','E005','E006','E007','E008','E009','E010',
                       'E011','E012','E013','E014','E015','E016','E017','E018','E019','E020','E021','E022','E023');

    SET v_rows = ROW_COUNT();

    -- 校验：老库应为 admin/manager + E001~E022 = 24 行；若 E023 账号已存在则为 25 行。
    --       少于 24 说明账号缺失或表结构漂移，报错中断，避免静默无操作。
    IF v_rows NOT BETWEEN 24 AND 25 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'fix_password_hashes: updated row count mismatch (expected 24~25)';
    END IF;

    -- 兜底：后补录员工（如 E023 赵保洁）的登录账号可能缺失，按「密码=工号」创建，
    --       确保 UPDATE 覆盖列表之外的员工账号也存在且可登录；已存在则跳过。
    INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
    SELECT e.store_id, e.employee_no,
           CASE e.employee_no
             WHEN 'E023' THEN '$2b$12$Y/M96/9xESm86gGkSP10mOeZjeYiGasg9w2WrtrnHV9qd7iVFFE52'
             ELSE ''  -- 未知工号的账号无可用初始密码，需人工设置后再登录
           END,
           e.name,
           CASE WHEN e.employee_no = 'E001' THEN 'STORE_MANAGER' ELSE 'EMPLOYEE' END,
           1, NOW(), NOW()
    FROM employees e
    WHERE e.status = 1
      AND e.employee_no BETWEEN 'E001' AND 'E023'
      AND NOT EXISTS (SELECT 1 FROM users u WHERE u.username = e.employee_no);

    INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
    VALUES (1, 1, '系统管理员', 'FIX_PASSWORD_HASHES', 'USER', NULL,
            '为 admin/manager/23名员工（E001~E023）设置独立真实 bcrypt 哈希，替代无效/共享占位哈希',
            'P0 安全修复：数据库无效/共享密码');

    COMMIT;
END$$
DELIMITER ;

CALL tmp_fix_password_hashes();
DROP PROCEDURE tmp_fix_password_hashes;
