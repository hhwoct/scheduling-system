-- ============================================================
-- 20260826: 排班偏好学习（店长习惯学习）
--
-- 背景：店长手动调整后发布的排班 = 对算法产出的「认可样本」。
--       本迁移建立学习数据底座：发布计划 → 聚合为员工偏好统计，
--       算法以偏好作为软排序因子，使生成结果越来越贴合店长习惯。
--       （功能开发中，feature/schedule-pref-learning 分支）
--
-- 内容：
--   1. employee_preferences：员工 × (班次/工作站/休息) × day_type 认可频次
--      - 样本来源：已发布(PUBLISHED)排班的 schedule_summaries
--      - 休息日样本：shift_code/workstation_id 均 NULL
--      - 由 PreferenceService.RebuildAsync 全量重建（幂等、自愈）
--   2. preference_trends：每期贴合率/覆盖率趋势（仪表盘用）
--   3. 规则 preference_weight（默认 0.3，0 = 关闭学习）
-- ============================================================
USE shift_mvp;

CREATE TABLE IF NOT EXISTS employee_preferences (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  employee_id BIGINT NOT NULL,
  day_type VARCHAR(20) NOT NULL,
  shift_code VARCHAR(20) NULL,
  workstation_id BIGINT NULL,
  freq INT NOT NULL DEFAULT 0,
  last_seen_at DATETIME NOT NULL,
  UNIQUE KEY uk_pref (store_id, employee_id, day_type, shift_code, workstation_id),
  INDEX idx_pref_emp (store_id, employee_id),
  CONSTRAINT fk_pref_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_pref_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_pref_ws FOREIGN KEY (workstation_id) REFERENCES workstations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS preference_trends (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  plan_id BIGINT NOT NULL,
  plan_name VARCHAR(200) NOT NULL,
  published_at DATETIME NOT NULL,
  adherence_pct DECIMAL(5,1) NOT NULL,
  coverage_pct DECIMAL(5,1) NOT NULL,
  sample_days INT NOT NULL,
  UNIQUE KEY uk_trend_plan (store_id, plan_id),
  CONSTRAINT fk_trend_store FOREIGN KEY (store_id) REFERENCES stores(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) 偏好权重规则：init 已有预留行（值 10），此处统一为 0（默认关闭，合并后行为不变，
--    需开启时在规则配置页将 preference_weight 设为 > 0）。
INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status)
SELECT 1, 'preference_weight', '偏好学习权重', '0', 'number', '排班偏好学习软约束权重（0=关闭；>0 启用，技能分相同时贴合店长历史习惯）', 1
WHERE NOT EXISTS (SELECT 1 FROM rule_configs WHERE store_id = 1 AND rule_key = 'preference_weight');

UPDATE rule_configs
SET rule_value = '0',
    rule_name = '偏好学习权重',
    remark = '排班偏好学习软约束权重（0=关闭；>0 启用，技能分相同时贴合店长历史习惯）'
WHERE store_id = 1 AND rule_key = 'preference_weight' AND (rule_value = '10' OR rule_value = '0.3');

-- 4) 调整量统计（增强 2）：生成时快照 + 每期调整数
--    schedule_plans.generated_summary_snapshot：生成时保存日汇总快照（JSON），
--    发布时 PreferenceService 对比当前汇总计算店长手动调整次数（员工×日期 维度）。
SET @col_exists := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'schedule_plans'
    AND COLUMN_NAME = 'generated_summary_snapshot');
SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE schedule_plans ADD COLUMN generated_summary_snapshot LONGTEXT NULL COMMENT ''生成时日汇总快照（JSON，用于计算店长调整量）'' AFTER published_at',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @col_exists := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'preference_trends'
    AND COLUMN_NAME = 'adjustments');
SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE preference_trends ADD COLUMN adjustments INT NOT NULL DEFAULT 0 COMMENT ''店长手动调整条数（员工×日期 维度，发布时计算）'' AFTER sample_days',
  'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
