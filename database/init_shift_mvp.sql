-- ================================================================
-- 排班系统数据库全量基线（建库建表 + 模拟数据）
-- 本脚本已吸收 20260822 及之前全部迁移的结构变更与数据（见文件末尾「已吸收迁移清单」）。
-- 新部署：直接执行本脚本即可，无需再跑被吸收的旧迁移。
-- 老库升级：不要执行本脚本，按序执行 database/migrations/ 下的迁移。
-- 20260823 起的迁移在两种路径下都仍须执行。
-- ⚠️ 安全：所有账号密码哈希均为占位符（见 users 种子数据处说明），部署前必须替换。
-- ================================================================
CREATE DATABASE IF NOT EXISTS shift_mvp DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE shift_mvp;

SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS peak_restricted_hours;
DROP TABLE IF EXISTS shift_swaps;
DROP TABLE IF EXISTS leave_requests;
DROP TABLE IF EXISTS ai_configs;
DROP TABLE IF EXISTS audit_logs;
DROP TABLE IF EXISTS notifications;
DROP TABLE IF EXISTS schedule_issues;
DROP TABLE IF EXISTS schedule_summaries;
DROP TABLE IF EXISTS schedule_results;
DROP TABLE IF EXISTS schedule_plans;
DROP TABLE IF EXISTS staffing_requirements;
DROP TABLE IF EXISTS date_parameters;
DROP TABLE IF EXISTS rule_configs;
DROP TABLE IF EXISTS employee_skills;
DROP TABLE IF EXISTS shift_workstations;
DROP TABLE IF EXISTS shift_templates;
DROP TABLE IF EXISTS workstations;
DROP TABLE IF EXISTS employees;
DROP TABLE IF EXISTS users;
DROP TABLE IF EXISTS stores;

SET FOREIGN_KEY_CHECKS = 1;

CREATE TABLE stores (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  code VARCHAR(50) NOT NULL UNIQUE,
  name VARCHAR(100) NOT NULL,
  address VARCHAR(255) NULL,
  max_employee_count INT NOT NULL DEFAULT 70,
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE users (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NULL,
  username VARCHAR(50) NOT NULL UNIQUE,
  password_hash VARCHAR(255) NOT NULL,
  nickname VARCHAR(100) NOT NULL,
  role VARCHAR(50) NOT NULL,
  status TINYINT NOT NULL DEFAULT 1,
  password_version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_users_store_id (store_id),
  CONSTRAINT fk_users_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE employees (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  employee_no VARCHAR(50) NOT NULL,
  name VARCHAR(100) NOT NULL,
  phone VARCHAR(30) NULL,
  department VARCHAR(50) NOT NULL,
  hire_date DATE NULL,
  primary_position VARCHAR(100) NULL,
  max_weekly_hours DECIMAL(5,2) NOT NULL DEFAULT 48.00,
  is_parttime TINYINT NOT NULL DEFAULT 0 COMMENT '是否兼职人员',
  is_generalist TINYINT NOT NULL DEFAULT 0 COMMENT '是否通岗（楼面低技能岗位通用）',
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_employees_store_no (store_id, employee_no),
  INDEX idx_employees_store_dept_status (store_id, department, status),
  CONSTRAINT fk_employees_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE workstations (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  code VARCHAR(50) NOT NULL,
  name VARCHAR(100) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  is_low_skill TINYINT NOT NULL DEFAULT 0 COMMENT '是否低技术含量岗位',
  remark VARCHAR(255) NULL,
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_workstations_store_code (store_id, code),
  INDEX idx_workstations_store_status (store_id, status),
  CONSTRAINT fk_workstations_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE shift_templates (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  code VARCHAR(20) NOT NULL,
  name VARCHAR(100) NOT NULL,
  start_time TIME NOT NULL,
  end_time TIME NOT NULL,
  is_cross_day TINYINT NOT NULL DEFAULT 0,
  priority INT NOT NULL DEFAULT 100,
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_shift_templates_store_code (store_id, code),
  INDEX idx_shift_templates_store_status (store_id, status),
  CONSTRAINT fk_shift_templates_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT chk_is_cross_day CHECK (is_cross_day IN (0, 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE shift_workstations (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  shift_template_id BIGINT NOT NULL,
  workstation_id BIGINT NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uk_shift_workstation (shift_template_id, workstation_id),
  INDEX idx_shift_workstations_workstation (workstation_id),
  CONSTRAINT fk_shift_workstations_shift FOREIGN KEY (shift_template_id) REFERENCES shift_templates(id),
  CONSTRAINT fk_shift_workstations_workstation FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE employee_skills (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  employee_id BIGINT NOT NULL,
  workstation_id BIGINT NOT NULL,
  skill_score INT NOT NULL DEFAULT 0,
  is_primary_skill TINYINT NOT NULL DEFAULT 0,
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_employee_workstation (employee_id, workstation_id),
  INDEX idx_employee_skills_workstation (workstation_id, skill_score),
  CONSTRAINT fk_employee_skills_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_employee_skills_workstation FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE rule_configs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  rule_key VARCHAR(100) NOT NULL,
  rule_name VARCHAR(100) NOT NULL,
  rule_value VARCHAR(100) NOT NULL,
  value_type VARCHAR(30) NOT NULL DEFAULT 'number',
  remark VARCHAR(255) NULL,
  status TINYINT NOT NULL DEFAULT 1,
  version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_rule_configs_store_key (store_id, rule_key),
  CONSTRAINT fk_rule_configs_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE date_parameters (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  work_date DATE NOT NULL,
  week_day INT NOT NULL,
  day_type VARCHAR(30) NOT NULL,
  is_legal_holiday TINYINT NOT NULL DEFAULT 0,
  is_holiday_eve TINYINT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uk_date_parameters_store_date (store_id, work_date),
  INDEX idx_date_parameters_store_type (store_id, day_type),
  CONSTRAINT fk_date_parameters_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE staffing_requirements (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  day_type VARCHAR(30) NOT NULL,
  workstation_id BIGINT NOT NULL,
  time_slot TIME NOT NULL,
  required_count INT NOT NULL DEFAULT 0,
  ideal_count INT NOT NULL DEFAULT 0,
  remark VARCHAR(200) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_staffing_req (store_id, day_type, workstation_id, time_slot),
  INDEX idx_staffing_req_store_type_slot (store_id, day_type, time_slot),
  CONSTRAINT fk_staffing_req_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_staffing_req_workstation FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE schedule_plans (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  plan_name VARCHAR(100) NOT NULL,
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'DRAFT',
  created_by BIGINT NULL,
  published_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY ux_schedule_plans_store_name (store_id, plan_name),
  UNIQUE KEY ux_schedule_plans_period (store_id, start_date, end_date),
  INDEX idx_schedule_plans_status (status),
  CONSTRAINT fk_schedule_plans_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_schedule_plans_user FOREIGN KEY (created_by) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE schedule_results (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  plan_id BIGINT NOT NULL,
  store_id BIGINT NOT NULL,
  employee_id BIGINT NOT NULL,
  work_date DATE NOT NULL,
  shift_template_id BIGINT NULL,
  time_slot TIME NOT NULL,
  workstation_id BIGINT NULL,
  skill_score INT NOT NULL DEFAULT 0,
  status VARCHAR(30) NOT NULL DEFAULT 'DRAFT',
  assignment_key VARCHAR(255)
    GENERATED ALWAYS AS (CONCAT(plan_id, '|', employee_id, '|', work_date, '|', time_slot)) STORED,
  version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_schedule_results_plan_date (plan_id, work_date),
  INDEX idx_schedule_results_employee_date (employee_id, work_date),
  INDEX idx_schedule_results_workstation_slot (workstation_id, work_date, time_slot),
  UNIQUE KEY ux_schedule_results_key (assignment_key),
  CONSTRAINT fk_schedule_results_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id),
  CONSTRAINT fk_schedule_results_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_schedule_results_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_schedule_results_shift FOREIGN KEY (shift_template_id) REFERENCES shift_templates(id),
  CONSTRAINT fk_schedule_results_workstation FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE schedule_summaries (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  plan_id BIGINT NOT NULL,
  store_id BIGINT NOT NULL,
  employee_id BIGINT NOT NULL,
  work_date DATE NOT NULL,
  is_rest_day TINYINT NOT NULL DEFAULT 0,
  shift_template_id BIGINT NULL,
  start_time TIME NULL,
  end_time TIME NULL,
  work_hours DECIMAL(5,2) NOT NULL DEFAULT 0.00,
  covered_workstations VARCHAR(500) NULL,
  break_start_time TIME NULL,
  break_end_time TIME NULL,
  break_cover_employee_id BIGINT NULL,
  break_workstation_id BIGINT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_schedule_summary (plan_id, employee_id, work_date),
  INDEX idx_schedule_summaries_plan_date (plan_id, work_date),
  CONSTRAINT chk_is_rest_day CHECK (is_rest_day IN (0, 1)),
  CONSTRAINT fk_schedule_summaries_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id),
  CONSTRAINT fk_schedule_summaries_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_schedule_summaries_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_schedule_summaries_shift FOREIGN KEY (shift_template_id) REFERENCES shift_templates(id),
  CONSTRAINT fk_schedule_summaries_cover_employee FOREIGN KEY (break_cover_employee_id) REFERENCES employees(id),
  CONSTRAINT fk_schedule_summaries_break_workstation FOREIGN KEY (break_workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE schedule_issues (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  plan_id BIGINT NOT NULL,
  store_id BIGINT NOT NULL,
  issue_type VARCHAR(50) NOT NULL,
  severity VARCHAR(20) NOT NULL DEFAULT 'WARN',
  work_date DATE NULL,
  time_slot TIME NULL,
  employee_id BIGINT NULL,
  workstation_id BIGINT NULL,
  description VARCHAR(500) NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'OPEN',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_schedule_issues_plan_type (plan_id, issue_type),
  INDEX idx_schedule_issues_date_slot (work_date, time_slot),
  CONSTRAINT fk_schedule_issues_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id),
  CONSTRAINT fk_schedule_issues_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_schedule_issues_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_schedule_issues_workstation FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE notifications (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  receiver_user_id BIGINT NULL,
  receiver_employee_id BIGINT NULL,
  notification_type VARCHAR(50) NOT NULL,
  title VARCHAR(200) NOT NULL,
  content VARCHAR(1000) NOT NULL,
  is_read TINYINT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  read_at DATETIME NULL,
  INDEX idx_notifications_store_read (store_id, is_read),
  INDEX idx_notifications_user (receiver_user_id),
  INDEX idx_notifications_employee (receiver_employee_id),
  CONSTRAINT fk_notifications_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_notifications_user FOREIGN KEY (receiver_user_id) REFERENCES users(id),
  CONSTRAINT fk_notifications_employee FOREIGN KEY (receiver_employee_id) REFERENCES employees(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE audit_logs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  operator_user_id BIGINT NULL,
  operator_name VARCHAR(100) NULL,
  action_type VARCHAR(80) NOT NULL,
  target_type VARCHAR(80) NOT NULL,
  target_id BIGINT NULL,
  before_content TEXT NULL,
  after_content TEXT NULL,
  remark VARCHAR(500) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_audit_logs_store_time (store_id, created_at),
  INDEX idx_audit_logs_action (action_type),
  CONSTRAINT fk_audit_logs_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_audit_logs_user FOREIGN KEY (operator_user_id) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 高峰禁休时段（20260817 并入基线）
-- 注意：TIME 类型无法表达跨午夜区间；仅支持单日内区间，故加 CHECK (start_time < end_time)。
CREATE TABLE peak_restricted_hours (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  start_time TIME NOT NULL,
  end_time TIME NOT NULL,
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_peak_hours_store (store_id),
  CONSTRAINT fk_peak_hours_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT chk_peak_hours_time_range CHECK (start_time < end_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 请假申请（20260807 并入基线）
CREATE TABLE leave_requests (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  employee_id BIGINT NOT NULL,
  leave_type VARCHAR(30) NOT NULL DEFAULT 'PERSONAL',
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  reason VARCHAR(500) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
  review_user_id BIGINT NULL,
  review_time DATETIME NULL,
  review_remark VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_leave_requests_employee (employee_id, start_date),
  INDEX idx_leave_requests_store_status (store_id, status),
  INDEX idx_leave_requests_status (status),
  INDEX idx_leave_requests_employee_dates (employee_id, start_date, end_date),
  CONSTRAINT fk_leave_requests_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_leave_requests_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_leave_requests_reviewer FOREIGN KEY (review_user_id) REFERENCES users(id),
  CONSTRAINT chk_leave_type CHECK (leave_type IN ('PERSONAL', 'SICK', 'ANNUAL')),
  CONSTRAINT chk_leave_date_range CHECK (start_date <= end_date),
  CONSTRAINT chk_leave_max_days CHECK (DATEDIFF(end_date, start_date) <= 30)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 换班申请（20260807 并入基线）
CREATE TABLE shift_swaps (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  plan_id BIGINT NOT NULL,
  requester_employee_id BIGINT NOT NULL,
  target_employee_id BIGINT NOT NULL,
  swap_date DATE NOT NULL,
  reason VARCHAR(500) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
  review_user_id BIGINT NULL,
  review_time DATETIME NULL,
  review_remark VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  pending_flag TINYINT
    GENERATED ALWAYS AS (IF(status = 'PENDING', 1, NULL)) STORED,
  INDEX idx_shift_swaps_requester (requester_employee_id, status),
  INDEX idx_shift_swaps_target (target_employee_id),
  INDEX idx_shift_swaps_store_status (store_id, status),
  INDEX idx_shift_swaps_plan_date (plan_id, swap_date),
  UNIQUE KEY ux_shift_swaps_pending (requester_employee_id, target_employee_id, plan_id, swap_date, pending_flag),
  CONSTRAINT fk_shift_swaps_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_shift_swaps_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id),
  CONSTRAINT fk_shift_swaps_requester FOREIGN KEY (requester_employee_id) REFERENCES employees(id),
  CONSTRAINT fk_shift_swaps_target FOREIGN KEY (target_employee_id) REFERENCES employees(id),
  CONSTRAINT fk_shift_swaps_reviewer FOREIGN KEY (review_user_id) REFERENCES users(id),
  CONSTRAINT chk_shift_swap_status CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
  CONSTRAINT chk_not_self CHECK (requester_employee_id <> target_employee_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO stores (id, code, name, address, max_employee_count, status) VALUES
(1, 'KM_GUNGUN', '昆明滚滚', '昆明市模拟门店地址', 70, 1);

-- 默认高峰禁休时段 20:00-22:00（20260817 并入基线）
INSERT INTO peak_restricted_hours (store_id, start_time, end_time) VALUES (1, '20:00:00', '22:00:00');

-- ⚠️ 安全警告：默认账户密码哈希已从脚本中移除（避免硬编码已知明文哈希）。
-- admin / manager 账号不再预插，部署时请创建账号并注入 BCrypt 哈希：
--  1. 手动执行：INSERT INTO users (store_id, username, password_hash, nickname, role, status) VALUES
--     (1, 'admin', '<BCRYPT_HASH>', '系统管理员', 'SYSTEM_ADMIN', 1),
--     (1, 'manager', '<BCRYPT_HASH>', '门店经理', 'STORE_MANAGER', 1);
--  2. 或由部署脚本读取环境变量 INIT_ADMIN_PASSWORD / INIT_MANAGER_PASSWORD 生成上述 SQL 注入。
-- 员工账号（E001~E023）以合法格式但不可登录的占位哈希创建，部署时替换，或执行
-- database/migrations/20260811_fix_password_hashes.sql（初始密码 = 工号，含 E023 账号兜底创建）。
-- 原占位哈希（含下划线、长度不足 60）非合法 bcrypt，且预插 admin/manager 会携带已知哈希，故已移除。

INSERT INTO workstations (store_id, code, name, sort_order, remark, status) VALUES
(1, 'MANAGER', '管理岗', 1, '门店管理与现场统筹', 1),
(1, 'CLERK_WAREHOUSE', '文员仓管', 2, '文员与仓管工作', 1),
(1, 'ENGINEERING', '工程维修岗', 3, '工程维修', 1),
(1, 'NETWORK', '网络维护', 4, '网络与设备维护', 1),
(1, 'PURCHASE', '采购岗', 5, '采购', 1),
(1, 'CLEANING', '保洁岗', 6, '保洁', 1),
(1, 'RECEPTION', '咨客岗', 7, '迎宾咨客', 1),
(1, 'KITCHEN', '厨房岗', 8, '厨房出品', 1),
(1, 'DELIVERY', '传送岗', 9, '传菜传送', 1),
(1, 'SERVICE', '服务岗', 10, '楼面服务', 1),
(1, 'CUSTOMER_MANAGER', '客户经理岗', 11, '客户经理', 1),
(1, 'INNER_BAR', '内吧岗', 12, '内吧', 1),
(1, 'OUTER_BAR', '外吧岗', 13, '外吧', 1);

INSERT INTO shift_templates (store_id, code, name, start_time, end_time, is_cross_day, priority, status) VALUES
(1, 'S1', '行政仓管/文员班', '13:00:00', '22:00:00', 0, 5, 1),
(1, 'S2', '行政工程A班', '13:00:00', '22:00:00', 0, 3, 1),
(1, 'S3', '行政工程B班', '16:00:00', '01:00:00', 1, 4, 1),
(1, 'S4', '楼面A班', '19:00:00', '04:00:00', 1, 7, 1),
(1, 'S5', '楼面B班', '21:00:00', '06:00:00', 1, 8, 1),
(1, 'S6', '吧台班', '18:30:00', '04:00:00', 1, 6, 1),
(1, 'S7', '厨房A班', '18:00:00', '03:00:00', 1, 1, 1),
(1, 'S8', '厨房B班', '19:00:00', '04:00:00', 1, 2, 1);

INSERT INTO shift_workstations (shift_template_id, workstation_id)
SELECT s.id, w.id FROM shift_templates s JOIN workstations w ON s.store_id = w.store_id
WHERE (s.code = 'S1' AND w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE'))
   OR (s.code = 'S2' AND w.code IN ('ENGINEERING','NETWORK'))
   OR (s.code = 'S3' AND w.code IN ('ENGINEERING','NETWORK'))
   OR (s.code = 'S4' AND w.code IN ('RECEPTION','DELIVERY','SERVICE','CUSTOMER_MANAGER'))
   OR (s.code = 'S5' AND w.code IN ('RECEPTION','DELIVERY','SERVICE','CUSTOMER_MANAGER','CLEANING'))
   OR (s.code = 'S6' AND w.code IN ('INNER_BAR','OUTER_BAR'))
   OR (s.code = 'S7' AND w.code IN ('KITCHEN'))
   OR (s.code = 'S8' AND w.code IN ('KITCHEN'));

INSERT INTO employees (store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, status) VALUES
(1, 'E001', '张店长', '13800000001', '管理', '2024-01-01', '管理岗', 48, 1),
(1, 'E002', '李文员', '13800000002', '行政', '2024-01-02', '文员仓管', 48, 1),
(1, 'E003', '王仓管', '13800000003', '行政', '2024-01-03', '文员仓管', 48, 1),
(1, 'E004', '赵工程', '13800000004', '工程', '2024-01-04', '工程维修岗', 48, 1),
(1, 'E005', '钱网络', '13800000005', '工程', '2024-01-05', '网络维护', 48, 1),
(1, 'E006', '孙采购', '13800000006', '行政', '2024-01-06', '采购岗', 48, 1),
(1, 'E007', '周保洁', '13800000007', '保洁', '2024-01-07', '保洁岗', 48, 1),
(1, 'E008', '吴咨客', '13800000008', '楼面', '2024-01-08', '咨客岗', 48, 1),
(1, 'E009', '郑咨客', '13800000009', '楼面', '2024-01-09', '咨客岗', 48, 1),
(1, 'E010', '冯厨房', '13800000010', '厨房', '2024-01-10', '厨房岗', 48, 1),
(1, 'E011', '陈厨房', '13800000011', '厨房', '2024-01-11', '厨房岗', 48, 1),
(1, 'E012', '褚厨房', '13800000012', '厨房', '2024-01-12', '厨房岗', 48, 1),
(1, 'E013', '卫传送', '13800000013', '楼面', '2024-01-13', '传送岗', 48, 1),
(1, 'E014', '蒋传送', '13800000014', '楼面', '2024-01-14', '传送岗', 48, 1),
(1, 'E015', '沈服务', '13800000015', '楼面', '2024-01-15', '服务岗', 48, 1),
(1, 'E016', '韩服务', '13800000016', '楼面', '2024-01-16', '服务岗', 48, 1),
(1, 'E017', '杨服务', '13800000017', '楼面', '2024-01-17', '服务岗', 48, 1),
(1, 'E018', '朱客户', '13800000018', '楼面', '2024-01-18', '客户经理岗', 48, 1),
(1, 'E019', '秦客户', '13800000019', '楼面', '2024-01-19', '客户经理岗', 48, 1),
(1, 'E020', '尤内吧', '13800000020', '吧台', '2024-01-20', '内吧岗', 48, 1),
(1, 'E021', '许外吧', '13800000021', '吧台', '2024-01-21', '外吧岗', 48, 1),
(1, 'E022', '何吧台', '13800000022', '吧台', '2024-01-22', '内吧岗', 48, 1),
(1, 'E023', '赵保洁', '13800000023', '保洁', '2024-01-23', '保洁岗', 48, 1);

-- 员工登录账号（E001~E023；E001 为店长）。⚠️ 占位符哈希（合法 bcrypt 格式，对应随机未知明文，
-- 无法登录），部署前必须替换为真实 BCrypt 哈希。
INSERT INTO users (store_id, username, password_hash, nickname, role, status)
SELECT 1, e.employee_no, '$2b$12$bV9nRYRvphTjN6EEDtrqEu9Ng5vh9vSQTujgBGFKYrmaxMsF2e2T.',
       e.name, CASE WHEN e.employee_no = 'E001' THEN 'STORE_MANAGER' ELSE 'EMPLOYEE' END, 1
FROM employees e
WHERE e.employee_no BETWEEN 'E001' AND 'E023';

INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id,
  CASE
    WHEN e.primary_position = w.name THEN 5
    WHEN e.department = '管理' AND w.code IN ('MANAGER','SERVICE','CUSTOMER_MANAGER') THEN 3
    WHEN e.department = '行政' AND w.code IN ('CLERK_WAREHOUSE','PURCHASE','MANAGER') THEN 3
    WHEN e.department = '工程' AND w.code IN ('ENGINEERING','NETWORK') THEN 4
    WHEN e.department = '保洁' AND w.code IN ('CLEANING','SERVICE') THEN 3
    WHEN e.department = '厨房' AND w.code = 'KITCHEN' THEN 5
    WHEN e.department = '楼面' AND w.code IN ('RECEPTION','DELIVERY','SERVICE','CUSTOMER_MANAGER') THEN 4
    WHEN e.department = '吧台' AND w.code IN ('INNER_BAR','OUTER_BAR') THEN 5
    ELSE 0
  END AS skill_score,
  CASE WHEN e.primary_position = w.name THEN 1 ELSE 0 END AS is_primary_skill,
  1
FROM employees e CROSS JOIN workstations w
WHERE e.store_id = 1 AND w.store_id = 1
  AND (
    (e.primary_position = w.name)
    OR (e.department = '管理' AND w.code IN ('MANAGER','SERVICE','CUSTOMER_MANAGER'))
    OR (e.department = '行政' AND w.code IN ('CLERK_WAREHOUSE','PURCHASE','MANAGER'))
    OR (e.department = '工程' AND w.code IN ('ENGINEERING','NETWORK'))
    OR (e.department = '保洁' AND w.code IN ('CLEANING','SERVICE'))
    OR (e.department = '厨房' AND w.code = 'KITCHEN')
    OR (e.department = '楼面' AND w.code IN ('RECEPTION','DELIVERY','SERVICE','CUSTOMER_MANAGER'))
    OR (e.department = '吧台' AND w.code IN ('INNER_BAR','OUTER_BAR'))
  );

INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status) VALUES
(1, 'default_monthly_rest_days', '默认每月休息天数', '4', 'number', 'MVP 默认每人每月休息 4 天', 1),
(1, 'max_weekly_hours', '最大周工时', '48', 'number', '默认最大周工时', 1),
(1, 'max_consecutive_work_days', '最大连续工作天数', '6', 'number', '连续工作超过该天数产生预警', 1),
(1, 'min_rest_hours_after_night_shift', '夜班后最小休息小时数', '10', 'number', '跨天夜班后的休息要求', 1),
(1, 'skill_match_weight', '技能匹配权重', '40', 'number', '算法软约束权重', 1),
(1, 'work_hour_balance_weight', '工时均衡权重', '30', 'number', '算法软约束权重', 1),
(1, 'preference_weight', '员工偏好权重', '10', 'number', 'MVP 预留', 1),
(1, 'station_continuity_weight', '工作站连续性权重', '20', 'number', '减少同日频繁换岗', 1);

-- 8 月日期参数。week_day 存 MySQL DAYOFWEEK 值（Sunday=1，Saturday=7）。
-- 业务口径（与 20260818 及算法 RestDayAllocator.IsPeakDay 一致）：
--   WEEKEND = 周五(6) + 周六(7)（晚市高峰日）；周日(1) 为工作日 WORKDAY。
-- 因此周末判据为 IN (6,7) 而非 IN (1,7)。2026 年 8 月无法定节假日，
--   is_legal_holiday 与 is_holiday_eve 均为 0（节前日仅在 9 月中秋前一日 09-24 单独标记）。
INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve)
SELECT 1, d, DAYOFWEEK(d), CASE WHEN DAYOFWEEK(d) IN (6,7) THEN 'WEEKEND' ELSE 'WORKDAY' END, 0, 0
FROM (
  SELECT DATE('2026-08-01') + INTERVAL seq DAY AS d
  FROM (
    SELECT 0 seq UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7
    UNION ALL SELECT 8 UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
    UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL SELECT 20 UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23
    UNION ALL SELECT 24 UNION ALL SELECT 25 UNION ALL SELECT 26 UNION ALL SELECT 27 UNION ALL SELECT 28 UNION ALL SELECT 29 UNION ALL SELECT 30
  ) x
) dates;

-- 2026-09 日期参数（含中秋：09-25/26 法定节假日，09-27 调休补班按平日，周五/周六为周末）
INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve)
SELECT
  1, d, DAYOFWEEK(d),
  CASE
    WHEN d IN ('2026-09-25', '2026-09-26') THEN 'HOLIDAY'
    WHEN d = '2026-09-27' THEN 'WORKDAY'
    WHEN DAYOFWEEK(d) IN (6, 7) THEN 'WEEKEND'
    ELSE 'WORKDAY'
  END,
  CASE WHEN d IN ('2026-09-25', '2026-09-26') THEN 1 ELSE 0 END,
  CASE WHEN d = '2026-09-24' THEN 1 ELSE 0 END
FROM (
  SELECT DATE('2026-09-01') + INTERVAL seq DAY AS d
  FROM (
    SELECT 0 seq UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7
    UNION ALL SELECT 8 UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
    UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL SELECT 20 UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23
    UNION ALL SELECT 24 UNION ALL SELECT 25 UNION ALL SELECT 26 UNION ALL SELECT 27 UNION ALL SELECT 28 UNION ALL SELECT 29
  ) x
) dates;

INSERT INTO staffing_requirements (store_id, day_type, workstation_id, time_slot, required_count)
SELECT 1, day_type, w.id, time_slot,
  CASE
    WHEN w.code IN ('MANAGER','CLERK_WAREHOUSE','PURCHASE','ENGINEERING','NETWORK') AND time_slot >= '13:00:00' AND time_slot < '22:00:00' THEN 1
    WHEN w.code = 'KITCHEN' AND time_slot >= '18:00:00' AND time_slot <= '23:30:00' THEN CASE WHEN day_type IN ('HOLIDAY','WEEKEND') THEN 3 ELSE 2 END
    WHEN w.code = 'KITCHEN' AND (time_slot >= '00:00:00' AND time_slot < '03:00:00') THEN 1
    WHEN w.code IN ('SERVICE','DELIVERY') AND (time_slot >= '19:00:00' AND time_slot <= '23:30:00') THEN CASE WHEN day_type IN ('HOLIDAY','WEEKEND') THEN 3 ELSE 2 END
    WHEN w.code IN ('SERVICE','DELIVERY') AND (time_slot >= '00:00:00' AND time_slot < '04:00:00') THEN CASE WHEN day_type IN ('HOLIDAY','WEEKEND') THEN 2 ELSE 1 END
    WHEN w.code IN ('RECEPTION','CUSTOMER_MANAGER') AND (time_slot >= '19:00:00' AND time_slot <= '23:30:00') THEN CASE WHEN day_type IN ('HOLIDAY','WEEKEND') THEN 2 ELSE 1 END
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (time_slot >= '18:30:00' AND time_slot <= '23:30:00') THEN CASE WHEN day_type IN ('HOLIDAY','WEEKEND') THEN 2 ELSE 1 END
    WHEN w.code IN ('INNER_BAR','OUTER_BAR') AND (time_slot >= '00:00:00' AND time_slot < '04:00:00') THEN 1
    WHEN w.code = 'CLEANING' AND (time_slot >= '21:00:00' AND time_slot <= '23:30:00' OR time_slot >= '00:00:00' AND time_slot < '06:00:00') THEN 1
    ELSE 0
  END
FROM workstations w
CROSS JOIN (SELECT 'WORKDAY' day_type UNION ALL SELECT 'WEEKEND' UNION ALL SELECT 'HOLIDAY') dt
CROSS JOIN (
  SELECT ADDTIME('00:00:00', SEC_TO_TIME(n * 1800)) AS time_slot
  FROM (
    SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7
    UNION ALL SELECT 8 UNION ALL SELECT 9 UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15
    UNION ALL SELECT 16 UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL SELECT 20 UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23
    UNION ALL SELECT 24 UNION ALL SELECT 25 UNION ALL SELECT 26 UNION ALL SELECT 27 UNION ALL SELECT 28 UNION ALL SELECT 29 UNION ALL SELECT 30 UNION ALL SELECT 31
    UNION ALL SELECT 32 UNION ALL SELECT 33 UNION ALL SELECT 34 UNION ALL SELECT 35 UNION ALL SELECT 36 UNION ALL SELECT 37 UNION ALL SELECT 38 UNION ALL SELECT 39
    UNION ALL SELECT 40 UNION ALL SELECT 41 UNION ALL SELECT 42 UNION ALL SELECT 43 UNION ALL SELECT 44 UNION ALL SELECT 45 UNION ALL SELECT 46 UNION ALL SELECT 47
  ) slots
) ts
WHERE w.store_id = 1;

-- 历史语义：最好人数 = 最少人数（20260819 起支持 (最少,最好) 两档）
UPDATE staffing_requirements SET ideal_count = required_count WHERE ideal_count = 0;

INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, target_id, after_content, remark)
VALUES (1, NULL, '系统管理员', 'INIT_DATABASE', 'DATABASE', NULL, '初始化 shift_mvp 数据库、核心表和模拟数据', '数据库初始化脚本执行完成');


-- ================================================================
-- 兼职人员 + 低技能岗位标记（并入基线）
-- （is_low_skill / is_parttime / is_generalist 列已并入上方 CREATE TABLE，此处仅回填标记）
-- ================================================================
UPDATE workstations SET is_low_skill = 1 WHERE code IN ('DELIVERY', 'CLEANING', 'SERVICE', 'RECEPTION');
INSERT INTO employees (store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, is_parttime, status) VALUES
(1, 'E101', '兼保洁A', '13900000101', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E102', '兼保洁B', '13900000102', '兼职', '2026-08-01', '保洁岗', 32, 1, 1),
(1, 'E103', '兼咨客A', '13900000103', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E104', '兼咨客B', '13900000104', '兼职', '2026-08-01', '咨客岗', 32, 1, 1),
(1, 'E105', '兼传送A', '13900000105', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E106', '兼传送B', '13900000106', '兼职', '2026-08-01', '传送岗', 32, 1, 1),
(1, 'E107', '兼服务A', '13900000107', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E108', '兼服务B', '13900000108', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E109', '兼服务C', '13900000109', '兼职', '2026-08-01', '服务岗', 32, 1, 1),
(1, 'E110', '兼服务D', '13900000110', '兼职', '2026-08-01', '服务岗', 32, 1, 1);
INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status)
SELECT e.id, w.id,
  CASE WHEN m.primary_position IS NULL THEN 2 ELSE 4 END,
  CASE WHEN m.primary_position IS NULL THEN 0 ELSE 1 END,
  1
FROM employees e CROSS JOIN workstations w
LEFT JOIN (
  SELECT '保洁岗' AS primary_position, 'CLEANING' AS code UNION ALL
  SELECT '咨客岗', 'RECEPTION' UNION ALL
  SELECT '传送岗', 'DELIVERY' UNION ALL
  SELECT '服务岗', 'SERVICE'
) m ON m.primary_position = e.primary_position AND m.code = w.code
WHERE e.store_id = 1 AND w.store_id = 1 AND e.is_parttime = 1 AND w.code IN ('CLEANING', 'RECEPTION', 'DELIVERY', 'SERVICE');

-- ================================================================
-- AI 文档识别配置（DeepSeek，20260820 并入基线）
-- 说明：api_key 明文保存（内部工具，GET 接口只返回掩码）；api_key='' 表示「未配置」
--      （后端 AiConfigService 将空串视为未配置，故不加 CHECK(api_key<>'') 以免破坏该语义）。
--      更优备选（不改变现有应用语义，供将来演进）：api_key 允许 NULL，NULL=未配置，加
--      CHECK (api_key IS NULL OR api_key <> '')。
--      生产建议由 KMS/密钥管理注入或应用层加密存储，数据库层无法强制。
-- ================================================================
CREATE TABLE ai_configs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  provider VARCHAR(30) NOT NULL DEFAULT 'DEEPSEEK',
  api_key VARCHAR(255) NOT NULL DEFAULT '',
  base_url VARCHAR(255) NOT NULL DEFAULT 'https://api.deepseek.com',
  model VARCHAR(80) NOT NULL DEFAULT 'deepseek-chat',
  status TINYINT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uk_ai_config_store_provider (store_id, provider),
  CONSTRAINT fk_ai_config_store FOREIGN KEY (store_id) REFERENCES stores(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ================================================================
-- 已吸收迁移清单（本基线执行后无需再执行；20260823 起的新迁移仍须按序执行）：
--   20260806_add_september_date_params.sql      → 09 月日期种子（上方，口径已按 20260818 修正）
--   20260806_harden_user_passwords.sql          → 占位符哈希 + 部署说明（安全考虑不再内置真实哈希）
--   20260807_add_employee_accounts.sql          → E001~E023 登录账号种子（占位符哈希）
--   20260807_add_leave_requests.sql             → leave_requests 表（含 CHECK/索引）
--   20260807_add_shift_swaps.sql                → shift_swaps 表（含 pending_flag/CHECK/索引）
--   20260811_add_password_version.sql           → users.password_version 列
--   20260811_add_rule_version.sql               → rule_configs.version 列
--   20260811_concurrency_constraints.sql        → 唯一约束与 CHECK 约束（并入各建表语句）
--   20260811_fix_p2_data_model.sql              → assignment_key 生成列/唯一索引、9 月日期（修正口径）
--   20260812_add_low_skill_workstation.sql      → workstations.is_low_skill 列与标记
--   20260813_add_parttime_employees.sql         → employees.is_parttime 列 + E101~E110 兼职
--   20260814_fix_admin_staffing_2200.sql        → 行政岗 22:00 边界（已体现在人数需求种子）
--   20260814_fix_e023_skills.sql                → E023 技能（由上方技能种子按部门自动覆盖）
--   20260817_add_break_and_peak_hours.sql       → break_* 列 + peak_restricted_hours 表 + 默认高峰时段
--   20260818_add_weekend_staffing.sql           → 周五/周六=WEEKEND 口径（09-27 补班标记已修正为非法定节假日）
--   20260819_add_ideal_count.sql                → ideal_count 列 + 回填
--   20260820_add_ai_config.sql                  → ai_configs 表
--   20260821_add_staffing_remark.sql            → staffing_requirements.remark 列
--   20260822_add_employee_generalist.sql        → employees.is_generalist 列
-- ================================================================
