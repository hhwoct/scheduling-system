-- 请假申请功能：员工提交请假，管理员审批
USE shift_mvp;

CREATE TABLE IF NOT EXISTS leave_requests (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  store_id BIGINT NOT NULL,
  employee_id BIGINT NOT NULL,
  leave_type VARCHAR(30) NOT NULL DEFAULT 'PERSONAL',   -- 事假/病假/年假等
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  reason VARCHAR(500) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'PENDING',        -- PENDING/APPROVED/REJECTED
  review_user_id BIGINT NULL,
  review_time DATETIME NULL,
  review_remark VARCHAR(255) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX idx_leave_requests_employee (employee_id, start_date),
  INDEX idx_leave_requests_store_status (store_id, status),
  CONSTRAINT fk_leave_requests_store FOREIGN KEY (store_id) REFERENCES stores(id),
  CONSTRAINT fk_leave_requests_employee FOREIGN KEY (employee_id) REFERENCES employees(id),
  CONSTRAINT fk_leave_requests_reviewer FOREIGN KEY (review_user_id) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
