<#
.SYNOPSIS
    排班系统 · 数据库初始化脚本（Windows PowerShell 5.1+ / PowerShell 7+）

.DESCRIPTION
    按 README「快速启动」的顺序建库、建表、跑增量迁移：
      init_shift_mvp.sql → 20260825~20260831 → 20260901_schedule_plan_source
      → 20260901_admin_global_rules（仅新库）→ 20260902+

.EXAMPLE
    .\scripts\setup-db.ps1

.EXAMPLE
    .\scripts\setup-db.ps1 -Reset

.EXAMPLE
    .\scripts\setup-db.ps1 -DbUser root -DbPassword root123 -SeedUsers .\seed-local-users.sql
#>
[CmdletBinding()]
param(
    [string]$DbHost = '127.0.0.1',
    [int]$Port = 3306,
    [string]$DbUser = 'root',
    [string]$DbPassword = $env:MYSQL_PWD,
    [switch]$Reset,
    [string]$SeedUsers
)

$ErrorActionPreference = 'Stop'
$Database = 'shift_mvp'          # init_shift_mvp.sql 里写死了这个库名

$RootDir = Split-Path -Parent $PSScriptRoot
$MigDir  = Join-Path $RootDir 'database' 'migrations'
$InitSql = Join-Path $RootDir 'database' 'init_shift_mvp.sql'

function Write-Step($text) { Write-Host $text }

# ---------- 找 mysql 客户端 ----------
$mysqlCmd = Get-Command mysql -ErrorAction SilentlyContinue
if (-not $mysqlCmd) {
    throw @"
找不到 mysql 客户端。
请先安装 MySQL 8.0（winget install Oracle.MySQL），并把它的 bin 目录加入 PATH，例如：
  C:\Program Files\MySQL\MySQL Server 8.0\bin
"@
}
$mysqlExe = $mysqlCmd.Source
if (-not $mysqlExe) { $mysqlExe = $mysqlCmd.Definition }

# ---------- 密码 ----------
if ([string]::IsNullOrEmpty($DbPassword)) {
    $secure = Read-Host -AsSecureString "请输入 MySQL 密码 ($DbUser@$DbHost)"
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $DbPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
$env:MYSQL_PWD = $DbPassword      # 避免命令行明文密码

$baseArgs = @('--default-character-set=utf8mb4', '-h', $DbHost, '-P', "$Port", '-u', $DbUser)

# ---------- 工具函数 ----------
function Invoke-MySql {
    param([string[]]$ExtraArgs = @(), [string]$Sql)
    $argv = $baseArgs + $ExtraArgs
    if ($Sql) { $argv += @('-e', $Sql) }
    $out = & $mysqlExe @argv
    if ($LASTEXITCODE -ne 0) { throw "mysql 执行失败（exit $LASTEXITCODE）" }
    return $out
}

function Invoke-Scalar {
    param([string]$Sql, [switch]$UseDatabase)
    $argv = $baseArgs
    if ($UseDatabase) { $argv = $argv + @($Database) }
    $argv = $argv + @('-N', '-B', '-e', $Sql)
    $out = & $mysqlExe @argv 2>$null
    if ($LASTEXITCODE -ne 0) { return '0' }
    return (($out | Out-String).Trim())
}

# 用 Start-Process 做输入重定向：按字节喂给 mysql，避免 PowerShell 管道改变 UTF-8 编码
function Invoke-SqlFile {
    param([string]$Path, [switch]$NoDatabase)

    $argv = $baseArgs
    if (-not $NoDatabase) { $argv = $argv + @($Database) }

    $outFile = [IO.Path]::GetTempFileName()
    $errFile = [IO.Path]::GetTempFileName()
    $display = Split-Path -Leaf $Path
    Write-Host ("  {0,-50}" -f $display) -NoNewline
    try {
        $proc = Start-Process -FilePath $mysqlExe -ArgumentList $argv `
            -RedirectStandardInput $Path -RedirectStandardOutput $outFile -RedirectStandardError $errFile `
            -NoNewWindow -Wait -PassThru
        if ($proc.ExitCode -eq 0) {
            Write-Host 'OK'
        } else {
            Write-Host 'FAIL'
            Get-Content $errFile -TotalCount 3 | ForEach-Object { Write-Host "      $_" }
            throw "执行 $display 失败"
        }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Get-MigrationFiles {
    Get-ChildItem -Path $MigDir -Filter '*.sql' | Sort-Object Name
}

function Get-DateKey($file) {
    $name = $file.Name
    return [int]$name.Substring(0, 8)
}

# ---------- 主流程 ----------
Write-Step '排班系统数据库初始化'
Write-Step "  连接: $DbUser@$DbHost`:$Port   库: $Database"

$dbExists = Invoke-Scalar "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='$Database';"
$tableCount = Invoke-Scalar "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='$Database';"
if ([string]::IsNullOrWhiteSpace($tableCount)) { $tableCount = '0' }

if ($Reset) {
    Write-Step '→ 删库重建'
    Invoke-MySql -Sql "DROP DATABASE IF EXISTS ``$Database``;" | Out-Null
    $full = $true
} elseif ($dbExists -eq '0' -or $tableCount -eq '0') {
    Write-Step '→ 库不存在或为空，执行首次初始化'
    $full = $true
} else {
    Write-Step "→ 库已存在（$tableCount 张表），只补跑迁移；要重建请加 -Reset"
    $full = $false
}

if ($full) {
    Invoke-SqlFile -Path $InitSql -NoDatabase
}

$migrations = Get-MigrationFiles

Write-Step '→ 执行增量迁移'
foreach ($f in $migrations) {
    $key = Get-DateKey $f
    if ($key -ge 20260825 -and $key -le 20260831) { Invoke-SqlFile -Path $f.FullName }
}

Invoke-SqlFile -Path (Join-Path $MigDir '20260901_schedule_plan_source.sql')
$store0 = Invoke-Scalar "SELECT COUNT(*) FROM stores WHERE id=0;" -UseDatabase
$globalRules = Join-Path $MigDir '20260901_admin_global_rules.sql'
if ($store0 -eq '0') {
    Invoke-SqlFile -Path $globalRules
} else {
    Write-Host ("  {0,-50}{1}" -f '20260901_admin_global_rules.sql', '跳过（全局门店 0 已存在）')
}

foreach ($f in $migrations) {
    if ((Get-DateKey $f) -ge 20260902) { Invoke-SqlFile -Path $f.FullName }
}

if ($SeedUsers) {
    if (-not (Test-Path $SeedUsers)) { throw "找不到账号种子文件: $SeedUsers" }
    Write-Step '→ 导入账号种子'
    Invoke-SqlFile -Path (Resolve-Path $SeedUsers).Path
}

Write-Step ''
Write-Step '完成。库内概况：'
Invoke-MySql -ExtraArgs @($Database) -Sql 'SELECT id, code, name FROM stores' | Write-Host
Invoke-MySql -ExtraArgs @($Database) -Sql 'SELECT store_id, COUNT(*) AS users FROM users GROUP BY store_id' | Write-Host

Write-Host @'

提示：
  - admin / manager 账号不在种子数据中（见 README「默认账号」），需自行生成 BCrypt 哈希后插入
  - 中文若乱码，说明客户端没带 --default-character-set=utf8mb4
  - 端口被占用：netstat -ano | findstr :5059，再 taskkill /PID <pid> /F
'@
