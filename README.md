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
  docs/                    项目文档
  README.md                项目启动说明
```

## 快速启动

### 1. 初始化数据库

```bash
mysql -u root -p < database/init_shift_mvp.sql
```

默认管理员账号：

| 用户名 | 密码（BCrypt 哈希，明文由开发环境配置） | 角色 |
|---|---|---|
| admin | 见开发环境配置 | 系统管理员 |
| manager | 见开发环境配置 | 门店经理 |

> 密码哈希通过 `database/migrations/20260806_harden_user_passwords.sql` 加固，work factor 12。

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

## 已实现 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/health | 健康检查 |
| POST | /api/auth/login | 管理员登录 |
| GET | /api/auth/me | 当前用户（需登录） |
| GET | /api/stores/current | 当前门店（需登录） |
| GET | /api/employees | 员工分页查询（需登录） |
| GET | /api/employees/{id} | 员工详情（需登录） |
| POST | /api/employees | 新增员工（需登录） |
| PUT | /api/employees/{id} | 编辑员工（需登录） |
| DELETE | /api/employees/{id} | 停用员工（需登录） |
| GET | /api/employees/{id}/skills | 员工技能矩阵（需登录） |
| PUT | /api/employees/{id}/skills | 保存员工技能（需登录） |
| GET | /api/workstations | 工作站列表（需登录） |
| PUT | /api/workstations/{id} | 编辑工作站（需登录） |
| GET | /api/shift-templates | 班次列表（需登录） |
| PUT | /api/shift-templates/{id} | 编辑班次（需登录） |
| GET | /api/rules | 规则配置列表（需登录） |
| PUT | /api/rules/{id} | 保存规则（需登录） |
| POST | /api/schedules/generate | 一键生成排班（需登录） |
| GET | /api/schedules | 排班计划列表（需登录） |
| GET | /api/schedules/{planId}/month-view | 月视图（需登录） |
| GET | /api/schedules/{planId}/week-view | 周视图（需登录） |
| GET | /api/schedules/{planId}/daily-view | 日明细（需登录） |
| GET | /api/schedules/{planId}/summary | 排班摘要（需登录） |
| PUT | /api/schedules/{planId}/adjust | 手动调整（需登录） |
| POST | /api/schedules/{planId}/publish | 发布排班（需登录） |

## 开发进度

- [x] 步骤 1：项目初始化（后端项目、前端项目、数据库目录、docs、README）
- [x] 步骤 2：数据库初始化（15 张表 + 模拟数据 + 密码加固迁移）
- [x] 步骤 3：后端基础框架（统一响应、全局异常、JWT 认证、当前用户、审计日志、16 张表 EF Core 映射）
- [x] 步骤 4：基础数据接口（员工 CRUD、技能矩阵、工作站、班次、规则，共 14 个接口）
- [x] 步骤 5：排班算法（三阶段：休息日分配、班次分配、连续性工作站分配 + 缺口/合规报告；阶段三已按需求文档 7.5 升级为完整 2A-2G：缺口块检测/块状借调/二次填补/逐段兜底，每人每天借调上限 2 次；5 个算法测试）
- [x] 步骤 6：排班业务接口（一键生成、月/周/日视图、手动调整、发布 + 站内通知，共 8 个接口）
- [x] 步骤 7~10：前端页面（登录/布局/员工/技能/工作站/班次/规则/一键排班/月/周/日视图/报表，共 10 个路由）
- [x] 步骤 11：联调测试（后端 5059 + 前端 5173 已跑通：健康检查、登录、员工、班次、一键生成、月/周/日视图、摘要；修复 DateOnly 启动崩溃和休息天数周期折算 bug）
