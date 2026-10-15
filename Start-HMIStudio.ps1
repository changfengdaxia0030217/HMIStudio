$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$webPath = Join-Path $root "src/HMI.Web"
$baseUrl = "http://127.0.0.1:5080"
$stdout = Join-Path $env:TEMP "hmistudio-server.log"
$stderr = Join-Path $env:TEMP "hmistudio-server-error.log"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "未找到 .NET SDK。请安装 .NET 10 SDK 后重试。" }
$sdkList = dotnet --list-sdks
if (-not ($sdkList | Where-Object { $_ -match '^10\.' })) { throw "未检测到 .NET 10 SDK。请安装 .NET 10 SDK 后重试。" }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw "未找到 npm。请安装 Node.js 20 或更高版本后重试。" }

Write-Host "[1/3] 检查前端依赖并构建..." -ForegroundColor Cyan
Push-Location $webPath
try {
    if (-not (Test-Path (Join-Path $webPath "node_modules"))) {
        npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm install 失败" }
    }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
}
finally { Pop-Location }

Write-Host "[2/3] 启动本机模拟运行时..." -ForegroundColor Cyan
$existing = $null
try { $existing = Invoke-RestMethod "$baseUrl/api/health" -TimeoutSec 2 } catch {}
if ($existing -and $existing.status -eq "ok") {
    Write-Host "HMIStudio 服务已在运行：$baseUrl" -ForegroundColor Yellow
    Start-Process $baseUrl
    exit 0
}

$server = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", "src/HMI.Server") -WorkingDirectory $root -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Write-Host "[3/3] 等待服务就绪..." -ForegroundColor Cyan
$health = $null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    try { $health = Invoke-RestMethod "$baseUrl/api/health" -TimeoutSec 2; if ($health.status -eq "ok") { break } } catch {}
    if ($server.HasExited) { break }
}
if (-not $health -or $health.status -ne "ok") {
    Get-Content $stdout -ErrorAction SilentlyContinue
    Get-Content $stderr -ErrorAction SilentlyContinue
    if (-not $server.HasExited) { & taskkill /PID $server.Id /T /F | Out-Null }
    throw "HMIStudio 未能启动。请检查上方输出及日志：$stdout"
}

Write-Host "HMIStudio 模拟运行已就绪：$baseUrl" -ForegroundColor Green
Write-Host "日志：$stdout"
Write-Host "停止服务：taskkill /PID $($server.Id) /T /F"
Start-Process $baseUrl
