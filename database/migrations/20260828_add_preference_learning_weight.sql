-- ============================================================
-- 20260828: 偏好学习权重规则改名
--
-- 背景：preference_weight 是 init 预留字段（MVP 预留，值 10），
--       名称有歧义（听起来像「员工偏好权重」，实际是「偏好学习开关」）。
--       正式更名为 preference_learning_weight：
--         0 = 偏好学习关闭（排班行为与算法规则一致）；
--         > 0（如 0.3）= 偏好学习启用（技能分相同时贴合店长历史习惯）。
--
-- 内容：
--   1. 复制旧规则的当前值到新规则（新装库：旧行被 20260826 置 0 → 复制 0；
--      已启用学习的库：复制启用值，无缝迁移）；
--   2. 停用旧规则行（保留历史，不删除）。
--
-- 幂等：NOT EXISTS 守卫 + 条件 UPDATE。
-- ============================================================
USE shift_mvp;

INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, remark, status)
SELECT old.store_id, 'preference_learning_weight', '偏好学习权重', old.rule_value, old.value_type,
       '排班偏好学习软约束权重（0=关闭；>0 启用，技能分相同时贴合店长历史习惯）', old.status
FROM rule_configs old
WHERE old.rule_key = 'preference_weight'
  AND NOT EXISTS (
    SELECT 1 FROM rule_configs n
    WHERE n.store_id = old.store_id AND n.rule_key = 'preference_learning_weight'
  );

UPDATE rule_configs
SET status = 0,
    remark = CONCAT(remark, '；已由 preference_learning_weight 取代（20260828）')
WHERE rule_key = 'preference_weight' AND status = 1;
