# -*- coding: utf-8 -*-
"""长沙滚滚（CSGG）门店导入脚本：解析 3 份真实排班 Excel -> 生成建店 + 真实班表 SQL。

用法:
  python3 scripts/import_csgg.py            # 生成 database/migrations/20260901_csgg_seed.sql
"""
import os, sys, json, datetime, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_dump import read as read_xlsx

try:
    import bcrypt
except ImportError:
    bcrypt = None

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_DIR = os.path.join(ROOT, '长沙滚滚')
OUT_SQL = os.path.join(ROOT, 'database', 'migrations', '20260901_csgg_seed.sql')

STORE_ID = 2
STORE_CODE = 'CSGG'
STORE_NAME = '长沙滚滚'
YEAR, MONTH, DAYS = 2026, 9, 30

WS = [  # id, code, name, sort
    (201, 'CSGG_FLOOR', '楼面', 1),
    (202, 'CSGG_DELIVERY', '传送', 2),
    (203, 'CSGG_KITCHEN', '厨房', 3),
    (204, 'CSGG_BAR', '民谣吧台', 4),
]
WS_BY_NAME = {n: i for i, c, n, s in WS}

SHIFTS = [  # id, code, name, start, end, cross_day, priority
    (201, 'A', 'A班', '15:00:00', '00:00:00', 1, 1),
    (202, 'B', 'B班', '18:00:00', '03:00:00', 1, 2),
]
SHIFT_ID = {'A': 201, 'B': 202}
SHIFT_SPAN = {'A': (15 * 60, 24 * 60), 'B': (18 * 60, 27 * 60)}  # 分钟, 可跨午夜
PLAN_ID = 5001

def q(s):
    if s is None:
        return 'NULL'
    return "'" + str(s).replace('\\', '\\\\').replace("'", "''") + "'"

def slots_of(shift_code):
    """返回该班次覆盖的 30 分钟槽位（跨午夜的槽位仍挂在同一 work_date 上，与现有引擎一致）。"""
    start, end = SHIFT_SPAN[shift_code]
    out = []
    m = start
    while m < end:
        mm = m % 1440
        out.append('%02d:%02d:00' % (mm // 60, mm % 60))
        m += 30
    return out

# ---------------- 解析 Excel ----------------
def first_sheet(book):
    for name, v in book.items():
        if any(any(c for c in r) for r in v['rows']):
            return v['rows']
    raise RuntimeError('空工作簿')

def parse_floor(path):
    """楼面表：空白=上班、休=休息，无班次标注 -> 统一按 A 班计。"""
    rows = first_sheet(read_xlsx(path))
    out = []
    for r in rows[3:]:
        if len(r) < 33:
            r = r + [''] * (33 - len(r))
        name, pos = r[1].strip(), r[2].strip()
        if not name or not pos:
            continue
        days = {}
        for d in range(1, DAYS + 1):
            v = r[2 + d].strip()
            days[d] = None if v == '休' else 'A'   # 真实表无班次标注，按 A 班计
        out.append({'name': name, 'station': pos, 'days': days, 'marked': False})
    return out

def parse_kitchen(path):
    """厨房表：A=早班 B=晚班 C=通班(已按需求删除) 休=休息；第 31 列忽略（9 月只有 30 天）。"""
    rows = first_sheet(read_xlsx(path))
    out = []
    for r in rows[1:]:
        name = r[0].strip()
        if not name or '早班' in name or '/' in name:
            continue
        days = {}
        for d in range(1, DAYS + 1):
            v = r[d].strip().upper()
            days[d] = None if (v == '休' or v == '') else ('B' if v in ('B', 'C') else 'A')
        out.append({'name': name, 'station': '厨房', 'days': days, 'marked': True})
    return out

def parse_bar(path):
    """吧台表：A=15:00-00:00 -> A班；C=18:00-03:00 -> B班；休=休息。"""
    rows = first_sheet(read_xlsx(path))
    out = []
    for r in rows[3:]:
        name = r[1].strip()
        if not name or '班：' in name or name.startswith('注'):
            continue
        days = {}
        for d in range(1, DAYS + 1):
            v = r[1 + d].strip().upper()
            days[d] = None if (v == '休' or v == '') else ('B' if v == 'C' else 'A')
        out.append({'name': name, 'station': '民谣吧台', 'days': days, 'marked': True})
    return out

def main():
    floor = parse_floor(os.path.join(DATA_DIR, '长沙滚滚楼面9月排班表(1).xlsx'))
    kitchen = parse_kitchen(os.path.join(DATA_DIR, '九月份厨房排班.xlsx'))
    bar = parse_bar(os.path.join(DATA_DIR, '长沙滚滚民谣吧台9月排班表.xlsx'))

    roster = [e for e in floor if e['station'] == '楼面'] \
           + [e for e in floor if e['station'] == '传送'] \
           + kitchen + bar
    for e in roster:
        if e['station'] not in WS_BY_NAME:
            raise RuntimeError('未知岗位: ' + e['station'])

    # 工号：A001 店长（不参与排班），员工 A002 起
    for idx, e in enumerate(roster):
        e['no'] = 'A%03d' % (idx + 2)
        e['emp_id'] = 2001 + idx
        e['user_id'] = 2001 + idx
    manager = {'no': 'A001', 'user_id': 2000, 'nickname': '店长'}

    # ---------- 真实覆盖 -> 人数需求 ----------
    def day_type(d):
        wd = datetime.date(YEAR, MONTH, d).isoweekday()
        return 'WEEKEND' if wd >= 6 else 'WORKDAY'

    cover = {}  # (day_type, ws_id, slot) -> {day: count}
    for e in roster:
        ws_id = WS_BY_NAME[e['station']]
        for d, code in e['days'].items():
            if not code:
                continue
            for s in slots_of(code):
                cover.setdefault((day_type(d), ws_id, s), {}).setdefault(d, 0)
                cover[(day_type(d), ws_id, s)][d] += 1
    days_by_type = {'WORKDAY': [], 'WEEKEND': []}
    for d in range(1, DAYS + 1):
        days_by_type[day_type(d)].append(d)

    staffing = []
    all_slots = ['%02d:%02d:00' % (h, m) for h in range(24) for m in (0, 30)]
    for dt in ('WORKDAY', 'WEEKEND'):
        for ws_id in [w[0] for w in WS]:
            for s in all_slots:
                per_day = cover.get((dt, ws_id, s), {})
                counts = [per_day.get(d, 0) for d in days_by_type[dt]]
                # required：取各工作日同类型天的中位在岗人数（比 min 更贴合真实常态），
                # ideal：取最大在岗人数（真实高峰），供「最好」档参考
                sc = sorted(counts)
                required = sc[len(sc) // 2] if sc else 0
                ideal = max(counts) if counts else 0
                if required or ideal:
                    staffing.append((dt, ws_id, s, required, ideal))

    # ---------- 生成 SQL ----------
    if bcrypt is None:
        raise RuntimeError('缺少 bcrypt: pip3 install bcrypt')
    def hash_of(no):
        return bcrypt.hashpw((no + '@123456').encode(), bcrypt.gensalt(12)).decode()

    L = []
    A = L.append
    A('-- 长沙滚滚（CSGG）门店建店 + 2026-09 真实班表导入')
    A('-- 由 scripts/import_csgg.py 从 长沙滚滚/*.xlsx 自动生成，可重复执行')
    A('SET NAMES utf8mb4;')
    A('SET FOREIGN_KEY_CHECKS = 0;')
    A('')
    A('-- 1. 清理旧数据（仅 store_id=%d）' % STORE_ID)
    for t in ['schedule_results', 'schedule_summaries', 'schedule_issues', 'schedule_plans',
              'staffing_requirements', 'peak_restricted_hours', 'rule_configs', 'date_parameters',
              'notifications', 'audit_logs']:
        A('DELETE FROM %s WHERE store_id = %d;' % (t, STORE_ID))
    A('DELETE FROM leave_requests WHERE employee_id IN (SELECT id FROM employees WHERE store_id = %d);' % STORE_ID)
    A('DELETE FROM shift_swaps WHERE store_id = %d;' % STORE_ID)
    A('DELETE FROM employee_skills WHERE employee_id IN (SELECT id FROM employees WHERE store_id = %d);' % STORE_ID)
    A('DELETE FROM employees WHERE store_id = %d;' % STORE_ID)
    A('DELETE FROM users WHERE store_id = %d;' % STORE_ID)
    A('DELETE FROM shift_workstations WHERE shift_template_id IN (SELECT id FROM shift_templates WHERE store_id = %d);' % STORE_ID)
    A('DELETE FROM shift_templates WHERE store_id = %d;' % STORE_ID)
    A('DELETE FROM workstations WHERE store_id = %d;' % STORE_ID)
    A('DELETE FROM stores WHERE id = %d OR code = %s;' % (STORE_ID, q(STORE_CODE)))
    A('')
    A('-- 2. 门店')
    A("INSERT INTO stores (id, code, name, address, max_employee_count, status) VALUES (%d, %s, %s, %s, 70, 1);"
      % (STORE_ID, q(STORE_CODE), q(STORE_NAME), q('湖南省长沙市')))
    A('')
    A('-- 3. 工作站（4 个岗位，无通岗）')
    for i, c, n, s in WS:
        A("INSERT INTO workstations (id, store_id, code, name, sort_order, is_low_skill, status) VALUES (%d, %d, %s, %s, %d, 0, 1);"
          % (i, STORE_ID, q(c), q(n), s))
    A('')
    A('-- 4. 班次模板（A班 15:00-00:00 / B班 18:00-03:00，均跨午夜；通班 C 按需求取消）')
    for i, c, n, st, et, cd, pr in SHIFTS:
        A("INSERT INTO shift_templates (id, store_id, code, name, start_time, end_time, is_cross_day, priority, status) VALUES (%d, %d, %s, %s, %s, %s, %d, %d, 1);"
          % (i, STORE_ID, q(c), q(n), q(st), q(et), cd, pr))
    for i, c, n, st, et, cd, pr in SHIFTS:
        for ws_id, _, _, _ in WS:
            A("INSERT INTO shift_workstations (shift_template_id, workstation_id) VALUES (%d, %d);" % (i, ws_id))
    A('')
    A('-- 5. 账号：A001 店长（不参与排班）+ A002~A%03d 员工，初始密码 工号@123456' % (len(roster) + 1))
    A("INSERT INTO users (id, store_id, username, password_hash, nickname, role, status, password_version) VALUES (%d, %d, %s, %s, %s, 'STORE_MANAGER', 1, 1);"
      % (manager['user_id'], STORE_ID, q(manager['no']), q(hash_of(manager['no'])), q(manager['nickname'])))
    for e in roster:
        A("INSERT INTO users (id, store_id, username, password_hash, nickname, role, status, password_version) VALUES (%d, %d, %s, %s, %s, 'EMPLOYEE', 1, 1);"
          % (e['user_id'], STORE_ID, q(e['no']), q(hash_of(e['no'])), q(e['name'])))
    A('')
    A('-- 6. 员工档案 + 技能（每人只会本岗位，不通岗）')
    for i, e in enumerate(roster):
        phone = '139%08d' % (int(e['no'][1:]) + 90000000 % 1)
        phone = '1390000' + e['no'][1:] + '1'
        A("INSERT INTO employees (id, store_id, employee_no, name, phone, department, hire_date, primary_position, max_weekly_hours, weekly_hours_follow_default, is_parttime, is_generalist, status) VALUES (%d, %d, %s, %s, %s, %s, '2025-01-01', %s, 60.00, 1, 0, 0, 1);"
          % (e['emp_id'], STORE_ID, q(e['no']), q(e['name']), q(phone), q(e['station']), q(e['station'])))
        A("INSERT INTO employee_skills (employee_id, workstation_id, skill_score, is_primary_skill, status) VALUES (%d, %d, 5, 1, 1);"
          % (e['emp_id'], WS_BY_NAME[e['station']]))
    A('')
    A('-- 7. 排班规则')
    rules = [('default_monthly_rest_days', '默认每月休息天数', '4', 'number'),
             ('max_weekly_hours', '最大周工时', '60', 'number'),
             ('max_consecutive_work_days', '最大连续工作天数', '14', 'number'),
             ('min_rest_hours_after_night_shift', '夜班后最小休息小时数', '10', 'number'),
             ('skill_match_weight', '技能匹配权重', '40', 'number'),
             ('work_hour_balance_weight', '工时均衡权重', '30', 'number'),
             ('preference_weight', '员工偏好权重', '0', 'number'),
             ('station_continuity_weight', '工作站连续性权重', '20', 'number'),
             ('min_daily_work_hours', '正式员工每日最低工时', '6.5', 'number'),
             ('preference_learning_weight', '偏好学习权重', '0', 'number'),
             ('max_daily_work_hours', '单日最大工时', '{"default":10}', 'json'),
             # 真实表未标注班次的岗位：楼面/传送 原表只有"休/空白"，导入时统一按 A 班计，
             # 对比算法结果时这些岗位只比"上班/休息"，不比班次。
             ('real_shift_unmarked_stations', '真实班表未标注班次的岗位', '楼面,传送', 'string')]
    for k, n, v, t in rules:
        A("INSERT INTO rule_configs (store_id, rule_key, rule_name, rule_value, value_type, status, version) VALUES (%d, %s, %s, %s, %s, 1, 1);"
          % (STORE_ID, q(k), q(n), q(v), q(t)))
    A("INSERT INTO peak_restricted_hours (store_id, start_time, end_time, status) VALUES (%d, '20:00:00', '22:00:00', 1);" % STORE_ID)
    A('')
    A('-- 8. 日期参数 2026-09（周一~周五 WORKDAY / 周六周日 WEEKEND）')
    for d in range(1, DAYS + 1):
        dt = datetime.date(YEAR, MONTH, d)
        A("INSERT INTO date_parameters (store_id, work_date, week_day, day_type, is_legal_holiday, is_holiday_eve) VALUES (%d, '%s', %d, %s, 0, 0);"
          % (STORE_ID, dt.isoformat(), dt.isoweekday(), q(day_type(d))))
    A('')
    A('-- 9. 人数需求（按真实排班的并发在岗人数统计：最少=同类日最小值，最好=最大值）')
    for dt, ws_id, s, req, ideal in staffing:
        A("INSERT INTO staffing_requirements (store_id, day_type, workstation_id, time_slot, required_count, ideal_count, remark) VALUES (%d, %s, %d, %s, %d, %d, %s);"
          % (STORE_ID, q(dt), ws_id, q(s), req, ideal, q('由2026-09真实排班统计')))
    A('')
    A('-- 10. 2026 年 9 月真实班表（导入，作为算法对比基线）')
    A("INSERT INTO schedule_plans (id, store_id, plan_name, start_date, end_date, status, created_by, published_at, source) VALUES (%d, %d, %s, '2026-09-01', '2026-09-30', 'PUBLISHED', %d, NOW(), 'REAL');"
      % (PLAN_ID, STORE_ID, q('2026年9月真实班表（Excel导入）'), manager['user_id']))
    n_res = 0
    for e in roster:
        ws_id = WS_BY_NAME[e['station']]
        for d in range(1, DAYS + 1):
            date = datetime.date(YEAR, MONTH, d).isoformat()
            code = e['days'][d]
            if not code:
                A("INSERT INTO schedule_summaries (plan_id, store_id, employee_id, work_date, is_rest_day, work_hours) VALUES (%d, %d, %d, '%s', 1, 0);"
                  % (PLAN_ID, STORE_ID, e['emp_id'], date))
                continue
            sid = SHIFT_ID[code]
            st, et = [x for x in SHIFTS if x[0] == sid][0][3:5]
            A("INSERT INTO schedule_summaries (plan_id, store_id, employee_id, work_date, is_rest_day, shift_template_id, start_time, end_time, work_hours, covered_workstations) VALUES (%d, %d, %d, '%s', 0, %d, %s, %s, 9.00, %s);"
              % (PLAN_ID, STORE_ID, e['emp_id'], date, sid, q(st), q(et), q(str(ws_id))))
            vals = []
            for s in slots_of(code):
                vals.append("(%d,%d,%d,'%s',%d,%s,%d,5,'PUBLISHED',1)" % (PLAN_ID, STORE_ID, e['emp_id'], date, sid, q(s), ws_id))
                n_res += 1
            A("INSERT INTO schedule_results (plan_id, store_id, employee_id, work_date, shift_template_id, time_slot, workstation_id, skill_score, status, version) VALUES " + ','.join(vals) + ';')
    A('')
    A('SET FOREIGN_KEY_CHECKS = 1;')

    os.makedirs(os.path.dirname(OUT_SQL), exist_ok=True)
    with open(OUT_SQL, 'w', encoding='utf-8') as f:
        f.write('\n'.join(L) + '\n')

    # ---------- 统计输出 ----------
    print('输出 SQL:', OUT_SQL)
    print('员工数: %d（楼面 %d / 传送 %d / 厨房 %d / 民谣吧台 %d）' % (
        len(roster), sum(1 for e in roster if e['station'] == '楼面'), sum(1 for e in roster if e['station'] == '传送'),
        sum(1 for e in roster if e['station'] == '厨房'), sum(1 for e in roster if e['station'] == '民谣吧台')))
    print('工号: %s ~ %s（A001=店长，不参与排班）' % (roster[0]['no'], roster[-1]['no']))
    print('真实班表明细行: %d，汇总行: %d' % (n_res, len(roster) * DAYS))
    print('人数需求行: %d' % len(staffing))
    for e in roster:
        rest = [d for d in range(1, DAYS + 1) if not e['days'][d]]
        a = sum(1 for d in range(1, DAYS + 1) if e['days'][d] == 'A')
        b = sum(1 for d in range(1, DAYS + 1) if e['days'][d] == 'B')
        print('  %s %-4s %-4s 休%2d天 A班%2d B班%2d 休息日:%s' % (e['no'], e['name'], e['station'], len(rest), a, b, ','.join(map(str, rest))))

main()
