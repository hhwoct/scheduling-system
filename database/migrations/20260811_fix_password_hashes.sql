USE shift_mvp;

-- ============================================================
-- P0-4 修复：数据库无效/共享密码
-- 为 admin、manager 和所有员工账号（E001~E023）设置独立真实的 bcrypt 哈希
-- 初始密码：admin=Admin@123456, manager=Manager@123456, 员工=工号@123456（如 E001@123456）
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
    DECLARE v_count BIGINT;
    DECLARE v_missing VARCHAR(500);
    DECLARE v_msg VARCHAR(500);
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;

    -- 1. 为已存在的账号设置独立 bcrypt 哈希。
    --    20260824 安全加固：仅当哈希为占位/非法（NULL、长度≠60、非 $2 前缀）时才替换——
    --    用户事后改过的合法哈希不被覆盖，迁移重跑安全（幂等）。
    UPDATE users
    SET password_hash = CASE username
        WHEN 'admin'   THEN '$2b$12$9p9nWbNUcDdJk9wAvqapge0GCFfX4quD8OH6XTJJVn9WPB2xORj12'
        WHEN 'manager' THEN '$2b$12$7O.K0j9MXlDUBm1q/8/a6uABHQLinhhc5Jc8jOUZwsLF0yjXbROqq'
        WHEN 'E001' THEN '$2b$12$fImXoamtF4gyg5QCATXZ4uWvk8yuYlH8xsqyXXsC1R2dTpJsFQylC'
        WHEN 'E002' THEN '$2b$12$MenE2Q3CNvJ6G08PExbNi.N5V1Tqx0NOe7YpwI4NegwF5hyZa93Gi'
        WHEN 'E003' THEN '$2b$12$ANY1bwC.6GuBzQLOI0USx.jqiFAWRi1HZLrGx10Vhfcy/iNUjULQq'
        WHEN 'E004' THEN '$2b$12$dl7AEVnOy6rU1k5Ejb5Y6uZ2yAMHMFe8t8j61oMjD0lum6j8z.rAe'
        WHEN 'E005' THEN '$2b$12$0ozrFOUTxgPrZ.u9bFfcGe3DLkkzSZvz.X.VT7zB5ZNXH/OveakFe'
        WHEN 'E006' THEN '$2b$12$GW0gRaKlTUr1kTpoAv9xW.8Nmjt/aLsd5/GjMmk7Wp4LgnXwhEG92'
        WHEN 'E007' THEN '$2b$12$NGRbGuxUO6evrLtzd6qggOmpncrHFKxjw3qX9SZNRLW5XLHWcbDiO'
        WHEN 'E008' THEN '$2b$12$0NgvR4vz.9DI3xnM9b2cuOOmmmtyPNOvdFCmji/djDuZw2LkRBT/O'
        WHEN 'E009' THEN '$2b$12$afo1LmZU5p5tJvcQbsr8EuxK18e3TmI6j81lb8v6za0j0.ZAjs/9y'
        WHEN 'E010' THEN '$2b$12$uDKBcdrrP1dKNckminloo.z5Ol7.Y1u4hVb8oADR/NeLQl8MOf4Te'
        WHEN 'E011' THEN '$2b$12$I97L9DsuPb5W8L321p/R4..Kjxw6WqfkJdX8lsA7OxpyLO5N50Ctq'
        WHEN 'E012' THEN '$2b$12$J9BLPOUnBXuPQURHpvyq3OG3w/y61S4J1uqXXe0FsZHQaXNYyj/Fm'
        WHEN 'E013' THEN '$2b$12$yIdpracjRBrPZJ58eo.FFOwHrFjL09yFYLIVNdP6WWlAviPrREtx6'
        WHEN 'E014' THEN '$2b$12$O.zvoYrv2GX1MFAJBPeL4uYWSuSNI5K.Vzs45UYD7awsKXycEHKXO'
        WHEN 'E015' THEN '$2b$12$QGgg/e585nl.PoOhTJupQ.LgoQZnO8ejUXvUmMwbxyPNMa7FJ4N8S'
        WHEN 'E016' THEN '$2b$12$/CGIErzygSLf275vESuyKuVM2oWquXxm5X5cKlna5OSWazTwha25m'
        WHEN 'E017' THEN '$2b$12$r2P1.PKtj5WSuv.daOy5QuXbgb3.lSTktChWM3zQ4Eh6CKe.CGSuq'
        WHEN 'E018' THEN '$2b$12$H/sOQMAt9X4VWuCVJtj7kuP5vr5vekm0Day/epGP/eSVToC2CyfAu'
        WHEN 'E019' THEN '$2b$12$TDmhf8OXhtbbcdZusORXfusqWngTOBtGGIhIZCggqeGDTJkvyVyXu'
        WHEN 'E020' THEN '$2b$12$hejims8NfbJzA8WSpo76X.04zFiO9CpnP73JISKZZ28qTxRX2ktKC'
        WHEN 'E021' THEN '$2b$12$xrJowbLODEKVxyatIcIofuWbMK6O2Ug2SbKOFPOWSi84Z0veZKtw6'
        WHEN 'E022' THEN '$2b$12$lbUOwlk/zPWtHzfYRMPYBeq4dpv1l47KM1jvKbmqLp81nYC3n0Qza'
        WHEN 'E023' THEN '$2b$12$o708qOwkmcxSt9je.ry5cOzy9sOYe3DgcJC.qSZs3qJqsjPtQ5/GO'
        ELSE password_hash
      END,
      updated_at = CURRENT_TIMESTAMP
    WHERE username IN ('admin', 'manager', 'E001','E002','E003','E004','E005','E006','E007','E008','E009','E010',
                       'E011','E012','E013','E014','E015','E016','E017','E018','E019','E020','E021','E022','E023')
      AND (password_hash IS NULL OR LENGTH(password_hash) <> 60 OR password_hash NOT LIKE '$2%');

    -- 1b. 审查修复（P1-1）：新版 init 不再预插 admin/manager，新装路径执行本迁移时
    --      兜底补建（初始密码与上方 CASE 一致），避免第 2 步 24 账号校验 SIGNAL 中断。
    INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
    SELECT 1, 'admin', '$2b$12$9p9nWbNUcDdJk9wAvqapge0GCFfX4quD8OH6XTJJVn9WPB2xORj12', '系统管理员', 'SYSTEM_ADMIN', 1, NOW(), NOW()
    WHERE NOT EXISTS (SELECT 1 FROM users WHERE username = 'admin');
    INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
    SELECT 1, 'manager', '$2b$12$7O.K0j9MXlDUBm1q/8/a6uABHQLinhhc5Jc8jOUZwsLF0yjXbROqq', '门店经理', 'STORE_MANAGER', 1, NOW(), NOW()
    WHERE NOT EXISTS (SELECT 1 FROM users WHERE username = 'manager');

    -- 2. 校验核心 24 账号（admin/manager/E001~E022）完整存在。
    --    用 COUNT(*) 而非 ROW_COUNT()：ROW_COUNT 只反映「变更行数」，重跑时已正确的哈希可能为 0，
    --    无法可靠反映「账号是否存在」。
    SELECT COUNT(*) INTO v_count
    FROM users
    WHERE username IN ('admin','manager','E001','E002','E003','E004','E005','E006','E007','E008','E009','E010',
                       'E011','E012','E013','E014','E015','E016','E017','E018','E019','E020','E021','E022');

    IF v_count <> 24 THEN
        -- 定位缺失的具体账号（admin/manager 缺失同样报错）
        SELECT GROUP_CONCAT(t.username ORDER BY t.username SEPARATOR ', ') INTO v_missing
        FROM (
            SELECT 'admin' AS username UNION ALL SELECT 'manager'
            UNION ALL SELECT 'E001' UNION ALL SELECT 'E002' UNION ALL SELECT 'E003'
            UNION ALL SELECT 'E004' UNION ALL SELECT 'E005' UNION ALL SELECT 'E006'
            UNION ALL SELECT 'E007' UNION ALL SELECT 'E008' UNION ALL SELECT 'E009'
            UNION ALL SELECT 'E010' UNION ALL SELECT 'E011' UNION ALL SELECT 'E012'
            UNION ALL SELECT 'E013' UNION ALL SELECT 'E014' UNION ALL SELECT 'E015'
            UNION ALL SELECT 'E016' UNION ALL SELECT 'E017' UNION ALL SELECT 'E018'
            UNION ALL SELECT 'E019' UNION ALL SELECT 'E020' UNION ALL SELECT 'E021'
            UNION ALL SELECT 'E022'
        ) t
        LEFT JOIN users u ON u.username = t.username
        WHERE u.username IS NULL;

        SET v_msg = CONCAT('fix_password_hashes: 核心账号缺失(', v_count, '/24): ', COALESCE(v_missing, ''));
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
    END IF;

    -- 3. E023 账号若缺失则补建（仅 E023；E001~E022 缺失已由上方 SIGNAL 报错，
    --    不再为其它工号生成空哈希账号）
    --    审查修复（P2）：老库升级时 E023 员工档案由更靠后的 20260814 迁移才补建，
    --    此处不再依赖 employees 表，员工存在时取档案信息、不存在时用默认值兜底。
    INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
    SELECT COALESCE((SELECT e.store_id FROM employees e WHERE e.employee_no = 'E023' LIMIT 1), 1),
           'E023',
           '$2b$12$o708qOwkmcxSt9je.ry5cOzy9sOYe3DgcJC.qSZs3qJqsjPtQ5/GO',
           COALESCE((SELECT e.name FROM employees e WHERE e.employee_no = 'E023' LIMIT 1), 'E023'),
           'EMPLOYEE', 1, NOW(), NOW()
    WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.username = 'E023');

    -- 4. 审计
    INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
    VALUES (1, 1, '系统管理员', 'FIX_PASSWORD_HASHES', 'USER', NULL,
            '为 admin/manager/23名员工（E001~E023）设置独立真实 bcrypt 哈希，替代无效/共享占位哈希',
            'P0 安全修复：数据库无效/共享密码');

    COMMIT;
END$$
DELIMITER ;

CALL tmp_fix_password_hashes();
DROP PROCEDURE tmp_fix_password_hashes;
