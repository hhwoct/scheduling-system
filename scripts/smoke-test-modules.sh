#!/bin/bash
# 各模块只读接口冒烟测试 (对运行中的 localhost:5059)
BASE=http://localhost:5059
BODY=/tmp/smoke_body.json

check() {
  local name="$1" path="$2" token="${3:-}"
  local code
  if [ -n "$token" ]; then
    code=$(curl -s -o "$BODY" -w '%{http_code}' -m 15 -H "Authorization: Bearer $token" "$BASE/api$path")
  else
    code=$(curl -s -o "$BODY" -w '%{http_code}' -m 15 "$BASE/api$path")
  fi
  local err=$(jq -r 'if type=="object" and .errorCode then .errorCode else "-" end' "$BODY" 2>/dev/null)
  local msg=$(jq -r 'if type=="object" and .message then .message[:60] else "-" end' "$BODY" 2>/dev/null)
  printf '%-28s | %-52s | HTTP %s | %s %s\n' "$name" "$path" "$code" "$err" "$msg"
}

echo "===== 登录 ====="
ADMIN_JSON=$(curl -s -m 15 -X POST $BASE/api/auth/login -H 'Content-Type: application/json' -d '{"username":"admin","password":"Admin@123456"}')
ADMIN_TOKEN=$(echo "$ADMIN_JSON" | jq -r '.data.token // .token // empty')
if [ -z "$ADMIN_TOKEN" ]; then echo "admin 登录失败: $(echo "$ADMIN_JSON" | head -c 300)"; exit 1; fi
echo "admin 登录成功"

E_JSON=$(curl -s -m 15 -X POST $BASE/api/auth/login -H 'Content-Type: application/json' -d '{"username":"E002","password":"E002@123456"}')
E_TOKEN=$(echo "$E_JSON" | jq -r '.data.token // .token // empty')
echo "E002 登录: $([ -n "$E_TOKEN" ] && echo 成功 || echo 失败)"

echo ""
echo "===== 认证/会话模块 ====="
check "auth/me" "/auth/me" "$ADMIN_TOKEN"

echo ""
echo "===== 门店/看板模块 ====="
check "stores/current" "/stores/current" "$ADMIN_TOKEN"
check "dashboard/stats" "/dashboard/stats" "$ADMIN_TOKEN"

echo ""
echo "===== 员工/技能模块 ====="
check "employees" "/employees?page=1&pageSize=5" "$ADMIN_TOKEN"
check "skill-matrix" "/skill-matrix" "$ADMIN_TOKEN"

echo ""
echo "===== 基础数据模块 ====="
check "workstations" "/workstations" "$ADMIN_TOKEN"
check "shift-templates" "/shift-templates" "$ADMIN_TOKEN"
check "rules" "/rules" "$ADMIN_TOKEN"
check "peak-restricted-hours" "/peak-restricted-hours" "$ADMIN_TOKEN"
check "staffing-requirements" "/staffing-requirements" "$ADMIN_TOKEN"
check "ai/config" "/ai/config" "$ADMIN_TOKEN"

echo ""
echo "===== 排班模块 ====="
PLAN_ID=$(curl -s -m 15 -H "Authorization: Bearer $ADMIN_TOKEN" "$BASE/api/schedules" | jq -r '.data.items[0].id // .data[0].id // empty')
echo "当前排班计划 planId=$PLAN_ID"
if [ -n "$PLAN_ID" ] && [ "$PLAN_ID" != "null" ]; then
  check "schedules list" "/schedules" "$ADMIN_TOKEN"
  check "month-view" "/schedules/$PLAN_ID/month-view" "$ADMIN_TOKEN"
  check "week-view" "/schedules/$PLAN_ID/week-view" "$ADMIN_TOKEN"
  check "daily-view" "/schedules/$PLAN_ID/daily-view?workDate=2026-08-28" "$ADMIN_TOKEN"
  check "summary" "/schedules/$PLAN_ID/summary" "$ADMIN_TOKEN"
  check "issues" "/schedules/$PLAN_ID/issues" "$ADMIN_TOKEN"
  check "rationality" "/schedules/$PLAN_ID/rationality" "$ADMIN_TOKEN"
fi

echo ""
echo "===== 偏好学习模块 ====="
check "preferences/stats" "/preferences/stats" "$ADMIN_TOKEN"
check "preferences/matrix" "/preferences/matrix" "$ADMIN_TOKEN"
check "preferences/top" "/preferences/top" "$ADMIN_TOKEN"
check "preferences/trends" "/preferences/trends" "$ADMIN_TOKEN"

echo ""
echo "===== 通知/审计模块 ====="
check "notifications" "/notifications" "$ADMIN_TOKEN"
check "notifications/unread-count" "/notifications/unread-count" "$ADMIN_TOKEN"
check "audit-logs" "/audit-logs?page=1&pageSize=5" "$ADMIN_TOKEN"

echo ""
echo "===== 审批模块 (admin 视图) ====="
check "leave-requests/review" "/leave-requests/review" "$ADMIN_TOKEN"
check "shift-swaps/review" "/shift-swaps/review" "$ADMIN_TOKEN"

echo ""
echo "===== 员工自助模块 (E002) ====="
if [ -n "$E_TOKEN" ]; then
  check "my-schedule" "/employee/my-schedule" "$E_TOKEN"
  check "leave-requests/mine" "/leave-requests/mine" "$E_TOKEN"
  check "shift-swaps/mine" "/shift-swaps/mine" "$E_TOKEN"
  check "notifications(员工)" "/notifications" "$E_TOKEN"
fi

echo ""
echo "===== 权限边界 (预期 401/403) ====="
check "无token访问 employees" "/employees" ""
check "员工访问管理接口" "/employees" "$E_TOKEN"
check "员工访问 dashboard" "/dashboard/stats" "$E_TOKEN"
