-- 换班申请功能：员工申请与同事换班，管理员审批后自动交换排班
USE shift_mvp;

CREATE TABLE IF NOT EXISTS shift_swaps (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  plan_id BIGINT NOT NULL,
  requester_employee_id BIGINT NOT NULL,
  target_employee_id BIGINT NOT NULL,
  swap_date DATE NOT NULL,
  reason VARCHAR(500) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'PENDING',        -- PENDING/APPROVED/REJECTED
  review_user_id BIGINT NULL,
  review_time DATETIME NULL,
  review_remark VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_shift_swaps_requester (requester_employee_id, status),
  INDEX idx_shift_swaps_target (target_employee_id),
  INDEX idx_shift_swaps_store_status (store_id, status),
  INDEX idx_shift_swaps_plan_date (plan_id, swap_date),
  CONSTRAINT fk_shift_swaps_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_shift_swaps_plan FOREIGN KEY (plan_id) REFERENCES schedule_plans(id),
  CONSTRAINT fk_shift_swaps_requester FOREIGN KEY (requester_employee_id) REFERENCES employees(id),
  CONSTRAINT fk_shift_swaps_target FOREIGN KEY (target_employee_id) REFERENCES employees(id),
  CONSTRAINT fk_shift_swaps_reviewer FOREIGN KEY (review_user_id) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;