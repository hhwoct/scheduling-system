# 排班系统 MVP

> 案例门店：昆明滚滚
> 后端：.NET 10.0 Web API + MySQL
> 前端：Vue 3 + Element Plus + Vite

## 项目结构

```text
排班系统/
  backend/                 后端 .NET Web API
    ShiftScheduling.Api/       Web API 主项目
    ShiftScheduling.Api.Tests/ 单元测试
  frontend/                前端 Vue 3 管理端
    src/                        源码
  database/                数据库初始化脚本和模拟数据
    init_shift_mvp.sql         建库建表 + 模拟数据
    migrations/                增量迁移脚本
    backups/                   备份文件
  docs/                    项目文档（交接/功能清单/接口文档/测试计划/测试报告/集成测试报告/全量测试报告/排班调整交互与偏好学习设计/安全审查）
  README.md                项目启动说明
```

## 功能特性

### 排班核心
- **一键生成排班**：自动完成四阶段分配（休息日 → 班次 → 工作站 → 班中休息），支持周视图/月视图/日明细
- **并发峰值需求计算**：按 (工作站, 时段) 并发需求排班，需求满足后多余员工（兼职）当天空闲
- **人数需求矩阵**：平日/周末/节假日 × 最少/最好人数两档，甘特图编辑 + 框选批量 + 格子备注 + Excel 导入导出 + AI 识别导入
- **日期参数（节假日/工作日）**：基础数据 → 日期参数页，按月日历在线维护 平日/周末/节假日，一键按规则补全未来 N 个月（已配置不覆盖），排班引擎严格按配置取日期类型
- **连续工作约束**：执行 `max_consecutive_work_days` 规则，连续工作达上限自动强制休息打断
- **班中休息**：30 分钟固定休息（<4 小时班不休息、避开高峰禁休时段），冗余不足借调顶岗（每人每天 ≤2 次），无人顶岗告警
- **高峰禁休时段**：门店可配多条（默认 20:00-22:00），班中休息不得与其重叠
- **按需自动补班次**：模板班次不足时按连续缺口块自动生成临时班次 D1/D2…（可跨午夜）
- **合理度趋势**：`/schedules/{planId}/rationality` 基于真实覆盖÷需求计算每日合理度（修复后 87%→97%，无人顶岗休息时段扣减覆盖）
- **问题诊断**：岗位缺口（含低技能兼职建议）/技能不匹配/工时超限/连续工作超限
- **发布确认**：存在 ERROR 问题时二次确认强制发布（force=true）
- **排班调整交互**（08-24/25）：日明细滑动选择范围 → 换人/左右平移/按人改休/取消排班；空位加人/撤加人（草稿与已发布均可，已发布自动通知员工）、满员行加人并存；全部操作支持撤销条 + Ctrl+Z/Cmd+Z 回退；同一员工当天泳道对齐固定行展示
- **发布前调整摘要 + 取消发布**：发布前对比生成快照差异（系统将学习什么）；已发布计划可退回草稿并通知员工
- **排班偏好学习**（默认关闭）：以店长手动调整后发布的排班为认可样本聚合偏好，`preference_learning_weight` 0-1 权重混合技能分与偏好分；管理端「偏好学习」页查看统计/矩阵/趋势，可重建聚合

### 员工与权限
- **全职 + 兼职**：员工标记 `is_parttime`，兼职仅可分配低技能岗位（保洁/咨客/传送/服务）
- **角色权限分离**：
  - SYSTEM_ADMIN（admin）：可修改排班规则/所有管理操作
  - STORE_MANAGER（店长 E001）：排班规则只读，其他管理可操作
- **技能等级总览**：员工 × 工作站技能矩阵色块编辑 + 通岗开关（楼面低技能岗位自动 ≥3 分）
- **员工自助**：员工端查看班表（含班中休息与「我顶岗的记录」）、提交请假、申请换班、**提前返岗**（缩短已批准请假）

### 安全加固（代码审查 P0/P1）
- 忘记密码「员工姓名 + 注册手机号」双验证直接重置（无短信/OTP 通道）+ 按 IP 限流 + 失败锁定（按姓名维度）；登录后右上角「修改密码」自助改密（手机号验证、旧密码错误固定时延、同密码拒绝）
- JWT 有效期默认 1440 分钟（24 小时，可在 Jwt:ExpireMinutes 配置 5~1440），前端另有 15 分钟无操作自动登出
- JWT 绑定 password_version：改密码后旧 Token 全部失效
- 员工手机号脱敏与格式校验；初始密码统一 `工号@123456`（已知明文，首登后须修改）
- 审计日志事务原子化
- 多租户 StoreId 隔离 + 并发唯一索引

## 快速启动

### 0. 测试环境准备（clone 后需配置两处）

出于安全考虑，`appsettings.Development.json` **不在仓库中**（已 gitignore），克隆后需自行创建：

```bash
cd backend/ShiftScheduling.Api
cat > appsettings.Development.json <<'EOF'
{
  "ConnectionStrings": { "ShiftMvp": "Server=localhost;Port=3306;Database=shift_mvp;User=root;Password=root123;Charset=utf8mb4;" },
  "Jwt": {
    "Issuer": "ShiftScheduling.Api",
    "Audience": "ShiftScheduling.Admin",
    "SigningKey": "<换成至少 32 字节的随机密钥>",
    "ExpireMinutes": 1440
  }
}
EOF
```

也可以改用 User Secrets 或环境变量注入 `ConnectionStrings:ShiftMvp` 与 `Jwt:SigningKey`。

前置条件（本机需已安装）：
- **MySQL 8.0**：默认连接 localhost:3306，账号 root / 密码 root123
- **.NET 10.0 SDK**
- **Node.js 18+**

> 若 MySQL 密码不同，请修改上面连接串中的 Password。

### 1. 初始化数据库

```bash
mysql -u root -p < database/init_shift_mvp.sql
```

默认账号（**init 脚本只创建员工账号且为不可登录的占位哈希；admin/manager 需按下方说明创建**）：

| 用户名 | 角色 |
|---|---|
| admin | 系统管理员（可改规则；审计日志等超管页面仅本账号可见，用户名由 `SuperAdminUsername` 配置） |
| manager / E001 | 店长 / 门店经理（规则只读；E001 排班为专职「店长」工作站，19:00-次日04:00 店长班，休息日由 E002/E003 轮替顶班） |
| E002~E023 | 全职员工 |
| E101~E110 | 兼职员工（仅排班数据，无登录账号） |

`init_shift_mvp.sql` 出于安全考虑不再内置真实密码哈希（历史哈希属已知明文，已从仓库移除），
也不再预插 `admin` / `manager` 账号。部署时请用 BCrypt（work factor 12）生成真实哈希后执行：

```sql
-- 创建管理员账号（已存在则更新其哈希）
INSERT INTO users (store_id, username, password_hash, nickname, role, status, created_at, updated_at)
VALUES (1, 'admin', '<BCRYPT_HASH>', '系统管理员', 'SYSTEM_ADMIN', 1, NOW(), NOW()),
       (1, 'manager', '<BCRYPT_HASH>', '门店经理', 'STORE_MANAGER', 1, NOW(), NOW())
ON DUPLICATE KEY UPDATE password_hash = VALUES(password_hash), updated_at = NOW();

-- 员工账号初始密码（占位哈希不可登录，替换为真实哈希）
UPDATE users SET password_hash = '<BCRYPT_HASH>' WHERE username BETWEEN 'E001' AND 'E023';
```

> 提示：若按顺序执行全部迁移，`database/migrations/20260811_fix_password_hashes.sql`
> 会把 admin/manager 重置为初始密码 `Admin@123456` / `Manager@123456`、员工重置为
> 「密码 = 工号@123456」（如 E001@123456），并兜底创建缺失的员工账号（如 E023）。初始密码属已知明文，
> **首次登录后请立即修改**。

### 2. 启动后端

```bash
cd backend/ShiftScheduling.Api
dotnet run
```

依赖环境变量或 User Secrets：

- `ConnectionStrings:ShiftMvp`：MySQL 连接字符串
- `Jwt:SigningKey`：至少 32 字节的 JWT 签名密钥

### 3. 启动前端

```bash
cd frontend
npm install
npm run dev
```

默认地址：http://localhost:5173 ，后端 API 端口为 5059，`/api` 会自动代理到 `http://localhost:5059`。

### 4. 后端测试

```bash
cd backend/ShiftScheduling.Api.Tests
dotnet test
```

### 5. 健康检查

```bash
curl http://localhost:5059/api/health
```

## 已实现 API（共 85 个端点，详见 docs/接口文档.md v1.3）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/health | 健康检查（无需认证） |
| POST | /api/auth/login | 登录（限流 5 次/分） |
| POST | /api/auth/forgot-password | 忘记密码（姓名+手机号双验证重置） |
| POST | /api/auth/change-password | 登录后自助修改密码（旧 Token 全失效） |
| GET | /api/auth/me | 当前用户（需登录） |
| GET | /api/stores/current | 当前门店（需登录） |
| GET | /api/dashboard/stats | 仪表盘统计（AdminOnly） |
| GET | /api/employees | 员工分页查询（手机号脱敏） |
| GET | /api/employees/{id} | 员工详情 |
| POST | /api/employees | 新增员工（手机号 11 位校验） |
| PUT | /api/employees/{id} | 编辑员工 |
| DELETE | /api/employees/{id} | 停用员工（软删除） |
| GET | /api/employees/{id}/skills | 员工技能矩阵 |
| PUT | /api/employees/{id}/skills | 保存员工技能 |
| GET | /api/skill-matrix | 技能等级总览（管理员+店长） |
| PUT | /api/skill-matrix/cell | 单格技能修改 |
| PUT | /api/skill-matrix/generalist | 通岗设置 |
| GET | /api/workstations | 工作站列表 |
| POST | /api/workstations | 新增工作站 |
| PUT | /api/workstations/{id} | 编辑工作站 |
| GET | /api/shift-templates | 班次列表 |
| PUT | /api/shift-templates/{id} | 编辑班次 |
| GET | /api/rules | 规则配置列表 |
| PUT | /api/rules/{id} | 保存规则（SystemAdminOnly + 乐观锁） |
| GET | /api/peak-restricted-hours | 高峰禁休时段列表 |
| POST | /api/peak-restricted-hours | 新增高峰时段 |
| PUT | /api/peak-restricted-hours/{id} | 修改/启停高峰时段 |
| DELETE | /api/peak-restricted-hours/{id} | 删除高峰时段 |
| GET | /api/staffing-requirements | 人数需求查询（三档 × 最少/最好） |
| PUT | /api/staffing-requirements | 批量保存人数需求 |
| GET | /api/staffing-requirements/preview | 周期需求预览 |
| GET | /api/date-parameters | 查询某月日期参数（节假日/工作日） |
| PUT | /api/date-parameters | 批量保存日期参数 |
| POST | /api/date-parameters/generate | 按周末规则补全未来 N 个月 |
| GET | /api/ai/config | 获取 AI 配置（Key 掩码） |
| PUT | /api/ai/config | 保存 AI 配置 |
| POST | /api/ai/test | AI 连通性测试 |
| POST | /api/ai/parse-requirement-doc | AI 识别人数需求文档 |
| POST | /api/schedules/generate | 一键生成排班（四阶段 + 临时班次） |
| GET | /api/schedules | 排班计划列表 |
| DELETE | /api/schedules/{planId} | 删除排班（级联） |
| GET | /api/schedules/{planId}/month-view | 月视图（含休息时段） |
| GET | /api/schedules/{planId}/week-view | 周视图（含休息/顶岗） |
| GET | /api/schedules/{planId}/daily-view | 日明细（含休息/顶岗） |
| GET | /api/schedules/{planId}/summary | 排班摘要 |
| GET | /api/schedules/{planId}/issues | 问题列表 |
| GET | /api/schedules/{planId}/rationality | 每日合理度 |
| PUT | /api/schedules/{planId}/adjust | 手动调整 |
| PUT | /api/schedules/{planId}/day-status | 批量设置休息/上班 |
| PUT | /api/schedules/{planId}/move-segment | 拖动移动工作段 |
| PUT | /api/schedules/{planId}/slot-status | 批量设置半小时休息状态 |
| POST | /api/schedules/{planId}/publish | 发布排班（force 强制） |
| POST | /api/schedules/{planId}/unpublish | 取消发布（退回草稿并通知员工） |
| GET | /api/schedules/{planId}/add-candidates | 空位加人候选 |
| PUT | /api/schedules/{planId}/add-slot | 空位加人（草稿/已发布） |
| PUT | /api/schedules/{planId}/remove-slot | 撤加人（已发布自动通知） |
| PUT | /api/schedules/{planId}/replace-slot | 范围换人（仅草稿） |
| PUT | /api/schedules/{planId}/move-range | 范围平移（仅草稿，±30 分钟） |
| PUT | /api/schedules/{planId}/clear-range | 取消排班（仅草稿） |
| POST | /api/schedules/{planId}/copy-previous | 复制上周（仅草稿，按星期几对齐） |
| GET | /api/schedules/{planId}/adjustments | 调整明细列表 |
| GET | /api/schedules/{planId}/adjustment-summary | 发布前调整摘要 |
| GET | /api/schedules/{planId}/demand-insights | 需求联动建议 |
| GET | /api/preferences/stats | 偏好统计（AdminOnly） |
| GET | /api/preferences/matrix | 偏好矩阵（AdminOnly） |
| GET | /api/preferences/top | 偏好排行（AdminOnly） |
| GET | /api/preferences/trends | 偏好趋势（AdminOnly） |
| POST | /api/preferences/rebuild | 重建偏好聚合（AdminOnly） |
| POST | /api/leave-requests | 提交请假 |
| GET | /api/leave-requests/mine | 我的请假列表 |
| GET | /api/leave-requests/review | 请假审批列表 |
| PUT | /api/leave-requests/{id}/review | 审批请假 |
| PUT | /api/leave-requests/{id}/early-return | 提前返岗 |
| POST | /api/shift-swaps | 提交换班申请 |
| GET | /api/shift-swaps/mine | 我的换班列表 |
| POST | /api/shift-swaps/candidates | 可换班同事 |
| GET | /api/shift-swaps/review | 换班审批列表 |
| PUT | /api/shift-swaps/{id}/review | 审批换班（并发原子） |
| GET | /api/notifications | 通知列表 |
| GET | /api/notifications/unread-count | 未读数量 |
| PUT | /api/notifications/{id}/read | 标记已读 |
| PUT | /api/notifications/read-all | 全部已读 |
| GET | /api/audit-logs | 审计日志查询 |
| GET | /api/employee/my-schedule | 员工端我的班表（含顶岗记录） |
| GET | /api/employee/swap-plans | 员工端可换班计划 |

## 开发进度

- [x] 步骤 1：项目初始化（后端项目、前端项目、数据库目录、docs、README）
- [x] 步骤 2：数据库初始化（15 张表 + 模拟数据 + 密码加固迁移）
- [x] 步骤 3：后端基础框架（统一响应、全局异常、JWT 认证、当前用户、审计日志、16 张表 EF Core 映射）
- [x] 步骤 4：基础数据接口（员工 CRUD、技能矩阵、工作站、班次、规则，共 14 个接口）
- [x] 步骤 5：排班算法（三阶段：休息日分配、班次分配、连续性工作站分配 + 缺口/合规报告；阶段三已按需求文档 7.5 升级为完整 2A-2G：缺口块检测/块状借调/二次填补/逐段兜底，每人每天借调上限 2 次；5 个算法测试）
- [x] 步骤 6：排班业务接口（一键生成、月/周/日视图、手动调整、发布 + 站内通知，共 8 个接口）
- [x] 步骤 7~10：前端页面（登录/布局/员工/技能/工作站/班次/规则/一键排班/月/周/日视图/报表，共 10 个路由）
- [x] 步骤 11：联调测试（后端 5059 + 前端 5173 已跑通：健康检查、登录、员工、班次、一键生成、月/周/日视图、摘要；修复 DateOnly 启动崩溃和休息天数周期折算 bug）
- [x] 步骤 12（08-17）：班中休息（阶段四，30 分钟固定休息 + 借调顶岗）+ 高峰禁休时段 + 安全加固（password_version/IP 限流/OTP/脱敏/并发审批）
- [x] 步骤 13（08-18）：人数需求三档两档（平日/周末/节假日 × 最少/最好）、人数需求甘特图页、通岗、技能矩阵总览、AI 文档识别（DeepSeek）、按需临时班次 D1/D2
- [x] 步骤 14（08-18/19）：二/三轮代码审查修复、引擎修复（选站丢员工、合理度 87%→97%、需求上限、OTP 绑定手机号）、测试套件扩展至 186 用例、WORKDAY 需求回填迁移
- [x] 步骤 15（08-25）：店长工作站（BOSS + 店长班 S10，E001 专职化、E002/E003 副手顶班）、残差补班段式裁剪修复、第四轮审查修复（员工编辑手机号脱敏回环、FORBIDDEN→403、超管用户名配置化、排班查看次日缺口漏标）、测试套件修复（编译恢复 + 190 用例全绿）
- [x] 步骤 16（08-24/25）：排班偏好学习（店长习惯学习，默认关闭、0-1 连续权重）+ 排班调整交互 P0~P2（撤销条/Ctrl+Z、发布前调整摘要、调整明细落库、需求联动建议、复制上周、批量改休、偏好角标）+ 范围操作接口（add/remove/replace-slot、move/clear-range）+ 取消发布 + 侧边栏可收起
- [x] 步骤 17（08-25）：自助修改密码（安全审查 P1-1，旧 Token 全失效）+ 忘记密码改「姓名+手机号」双验证 + 员工初始密码统一 `工号@123456` + 第三/四轮全项目审查修复（234 测试全绿 + E2E）
- [x] 步骤 18（08-27）：日明细员工泳道对齐 + 满员行加人 + 通知「一键已读」口径修复（read-all）+ 06:00 幽灵时段修复（引擎输入净化 + 迁移清理，235 测试全绿）+ 全量测试报告（234 单测 + 34 接口冒烟 + 56 浏览器 E2E 全部通过）