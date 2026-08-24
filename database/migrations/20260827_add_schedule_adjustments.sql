-- ============================================================
-- 20260827: 排班调整明细（店长修改全程记录）
--
-- 背景：店长的每一次手动调整都是「纠错信号」。
--       此前仅审计文本 + 调整计数，缺少结构化的 before/after 明细。
--       本表记录每次调整：谁、何时、把哪个员工从什么改成什么。
--
-- 用途：
--   1. 可追溯：发布前查看本期全部调整明细；
--   2. 学习信号：纠错方向（如「把 S7 换成 S8」）可升级偏好学习；
--   3. 需求联动（P1）：同类调整反复出现时提示调整人数需求配置。
--
-- 写入点：move-segment / day-status / slot-status / adjust 端点，
--         与业务数据同事务（调整成功即记录）。
-- ============================================================
USE shift_mvp;

CREATE TABLE IF NOT EXISTS schedule_adjustments (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  plan_id BIGINT NOT NULL,
  employee_id BIGINT NULL,
  work_date DATE NULL,
  time_slot TIME NULL,
  action_type VARCHAR(30) NOT NULL,
  before_json VARCHAR(1000) NULL,
  after_json VARCHAR(1000) NULL,
  operator_user_id BIGINT NULL,
  operator_name VARCHAR(50) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_adj_plan (plan_id, created_at),
  INDEX idx_adj_store_time (store_id, created_at),
  CONSTRAINT fk_adj_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_adj_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
