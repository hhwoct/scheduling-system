#!/usr/bin/env bash
# ============================================================
# 排班系统 · 数据库初始化脚本（macOS / Linux / Git Bash / WSL）
#
# 用法：
#   ./scripts/setup-db.sh                    # 库不存在则建库 + 跑迁移；已存在只补迁移
#   ./scripts/setup-db.sh --reset            # 删库重建（会清空数据）
#   ./scripts/setup-db.sh -u root -p root123 -h 127.0.0.1 -P 3306
#   ./scripts/setup-db.sh --seed-users ./seed-local-users.sql   # 可选：额外导入账号种子
#
# 前提：PATH 里能调用 MySQL 8.0 的 mysql 客户端
# ============================================================
set -euo pipefail

DB_HOST="${MYSQL_HOST:-127.0.0.1}"
DB_PORT="${MYSQL_PORT:-3306}"
DB_USER="${MYSQL_USER:-root}"
DB_PASSWORD="${MYSQL_PWD:-root123}"
RESET=0
SEED_USERS=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --reset)       RESET=1; shift ;;
    -h|--host)     DB_HOST="$2"; shift 2 ;;
    -P|--port)     DB_PORT="$2"; shift 2 ;;
    -u|--user)     DB_USER="$2"; shift 2 ;;
    -p|--password) DB_PASSWORD="$2"; shift 2 ;;
    --seed-users)  SEED_USERS="$2"; shift 2 ;;
    --help)        sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "未知参数: $1（用 --help 查看用法）" >&2; exit 1 ;;
  esac
done

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIG_DIR="$ROOT_DIR/database/migrations"
DB_NAME="shift_mvp"          # init_shift_mvp.sql 里写死了这个库名
export MYSQL_PWD="$DB_PASSWORD"   # 避免命令行明文密码告警

if ! command -v mysql >/dev/null 2>&1; then
  echo "✗ 找不到 mysql 客户端，请先安装 MySQL 8.0 并加入 PATH" >&2
  exit 1
fi

# 必须统一 utf8mb4，否则中文会被双重编码
my() { mysql --default-character-set=utf8mb4 -h "$DB_HOST" -P "$DB_PORT" -u "$DB_USER" "$@"; }

run_sql() {   # run_sql <文件> [库名]；不传库名表示脚本自带 CREATE DATABASE / USE
  local file=$1 db=${2:-}
  printf "  %-50s" "$(basename "$file")"
  local ok
  if [[ -n "$db" ]]; then
    my "$db" <"$file" >/tmp/setupdb.out 2>/tmp/setupdb.err && ok=1 || ok=0
  else
    my <"$file" >/tmp/setupdb.out 2>/tmp/setupdb.err && ok=1 || ok=0
  fi
  if [[ $ok -eq 1 ]]; then echo "OK"; else echo "FAIL"; head -3 /tmp/setupdb.err; return 1; fi
}

echo "排班系统数据库初始化"
echo "  连接: ${DB_USER}@${DB_HOST}:${DB_PORT}   库: ${DB_NAME}"

DB_EXISTS=$(my -N -B -e "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='${DB_NAME}';" | tr -d '[:space:]')
TABLE_COUNT=$(my -N -B -e "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='${DB_NAME}';" 2>/dev/null | tr -d '[:space:]' || echo 0)

if [[ $RESET -eq 1 ]]; then
  echo "→ 删库重建"
  my -e "DROP DATABASE IF EXISTS \`${DB_NAME}\`;"
  FULL=1
elif [[ "${DB_EXISTS:-0}" == "0" || "${TABLE_COUNT:-0}" == "0" ]]; then
  echo "→ 库不存在或为空，执行首次初始化"
  FULL=1
else
  echo "→ 库已存在（${TABLE_COUNT} 张表），只补跑迁移；要重建请加 --reset"
  FULL=0
fi

if [[ $FULL -eq 1 ]]; then
  printf "  %-50s" "init_shift_mvp.sql"
  if my <"$ROOT_DIR/database/init_shift_mvp.sql" >/tmp/setupdb.out 2>/tmp/setupdb.err; then echo "OK"; else echo "FAIL"; head -5 /tmp/setupdb.err; exit 1; fi
fi

ALL_MIGRATIONS=$(ls "$MIG_DIR"/*.sql | sort)

echo "→ 执行增量迁移"
# 20260825 ~ 20260831（20260824 及以前已并入 init 基线）
for f in $ALL_MIGRATIONS; do
  b=$(basename "$f"); d=${b:0:8}
  (( 10#$d >= 20260825 && 10#$d <= 20260831 )) || continue
  run_sql "$f" "$DB_NAME"
done

# 20260901：先加 source 列，再跑全局规则（规则脚本里的「上移」不幂等，只在新库执行）
run_sql "$MIG_DIR/20260901_schedule_plan_source.sql" "$DB_NAME"
STORE0=$(my -N -B "$DB_NAME" -e "SELECT COUNT(*) FROM stores WHERE id=0;" | tr -d '[:space:]')
if [[ "${STORE0:-0}" == "0" ]]; then
  run_sql "$MIG_DIR/20260901_admin_global_rules.sql" "$DB_NAME"
else
  printf "  %-50s%s\n" "20260901_admin_global_rules.sql" "跳过（全局门店 0 已存在）"
fi

# 20260902 及以后
for f in $ALL_MIGRATIONS; do
  b=$(basename "$f"); d=${b:0:8}
  (( 10#$d >= 20260902 )) || continue
  run_sql "$f" "$DB_NAME"
done

if [[ -n "$SEED_USERS" ]]; then
  echo "→ 导入账号种子"
  run_sql "$SEED_USERS" "$DB_NAME"
fi

echo
echo "完成。库内概况："
my "$DB_NAME" -e "
SELECT id, code, name FROM stores;
SELECT store_id, COUNT(*) AS employees FROM employees GROUP BY store_id;
SELECT store_id, COUNT(*) AS users FROM users GROUP BY store_id;" || true

cat <<'TIP'

提示：
  - admin / manager 账号不在种子数据中（见 README「默认账号」），需自行生成 BCrypt 哈希后插入
  - 中文若乱码，说明客户端没带 --default-character-set=utf8mb4
TIP
