USE shift_mvp;

-- 班中休息功能：schedule_summaries 增加休息时段与顶岗字段；
-- 新增高峰禁休时段表 peak_restricted_hours（admin 端增删改查）。
-- 审查修复（P2-10 注释）：MySQL DDL 会隐式提交，START TRANSACTION 无法保证
-- 本脚本原子性，仅作为"整批执行"的语义标记保留。
START TRANSACTION;

-- 1. 班中休息字段（30 分钟固定休息；break_cover_employee_id = NULL 表示无人顶岗）
ALTER TABLE schedule_summaries
  ADD COLUMN break_start_time time DEFAULT NULL COMMENT '班中休息开始时间' AFTER covered_workstations,
  ADD COLUMN break_end_time time DEFAULT NULL COMMENT '班中休息结束时间' AFTER break_start_time,
  ADD COLUMN break_cover_employee_id bigint DEFAULT NULL COMMENT '顶岗员工ID（NULL=无人顶岗）' AFTER break_end_time,
  ADD COLUMN break_workstation_id bigint DEFAULT NULL COMMENT '休息时所在工作站（主工作站）' AFTER break_cover_employee_id,
  ADD CONSTRAINT fk_schedule_summaries_cover_employee FOREIGN KEY (break_cover_employee_id) REFERENCES employees (id),
  ADD CONSTRAINT fk_schedule_summaries_break_workstation FOREIGN KEY (break_workstation_id) REFERENCES workstations (id);

-- 2. 高峰禁休时段表（按门店配置，可多条）
-- 注意：TIME 类型无法表达跨午夜区间；本表仅支持单日内区间，故加 CHECK (start_time < end_time)。
--       约束名为 chk_peak_hours_time_range；将来若需支持跨午夜，可 DROP 该 CHECK 后改建模。
--       跨午夜高峰需求请拆成两条（如 23:00-24:00 与 00:00-01:00）。
CREATE TABLE IF NOT EXISTS peak_restricted_hours (
  id bigint NOT NULL AUTO_INCREMENT,
  store_id bigint NOT NULL,
  start_time time NOT NULL COMMENT '高峰开始（含）',
  end_time time NOT NULL COMMENT '高峰结束（不含）',
  status tinyint NOT NULL DEFAULT 1 COMMENT '1启用 0停用',
  created_at datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_peak_hours_store (store_id),
  CONSTRAINT fk_peak_hours_store FOREIGN KEY (store_id) REFERENCES stores (id),
  CONSTRAINT chk_peak_hours_time_range CHECK (start_time < end_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. 为所有现有门店插入默认高峰时段 20:00-22:00
INSERT INTO peak_restricted_hours (store_id, start_time, end_time)
SELECT id, '20:00:00', '22:00:00' FROM stores;

-- 4. 审计
INSERT INTO audit_logs (store_id, operator_user_id, operator_name, action_type, target_type, remark)
VALUES (1, 1, '系统管理员', 'MIGRATE_BREAK_PEAK_HOURS', 'SCHEMA',
        '新增班中休息字段与高峰禁休时段表，默认高峰时段 20:00-22:00');

COMMIT;
