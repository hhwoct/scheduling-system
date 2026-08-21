-- 请假表新增"是否提前返岗"标记：员工提前返岗时置 1（原结束日期仅缩短，无额外记录）
ALTER TABLE leave_requests
  ADD COLUMN early_returned TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否提前返岗（1=是）' AFTER review_remark;
