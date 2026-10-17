$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$webPath = Join-Path $root "src/HMI.Web"
$serverProject = Join-Path $root "src/HMI.Server/HMI.Server.csproj"
$artifactsPath = Join-Path $root "artifacts"
$publishPath = Join-Path $artifactsPath "HMIStudio-win-x64"
$zipPath = Join-Path $artifactsPath "HMIStudio-win-x64.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "未找到 .NET SDK。请安装 .NET 10 SDK 后重试。" }
if (-not ((dotnet --list-sdks) | Where-Object { $_ -match '^10\.' })) { throw "未检测到 .NET 10 SDK。" }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw "未找到 npm。请安装 Node.js 20 或更高版本后重试。" }

Write-Host "[1/4] 构建 React/TypeScript 前端..." -ForegroundColor Cyan
Push-Location $webPath
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci 失败" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
}
finally { Pop-Location }

Write-Host "[2/4] 发布 Windows x64 自包含应用..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null
if (Test-Path $publishPath) { Remove-Item $publishPath -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

dotnet publish $serverProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false --output $publishPath
if ($LASTEXITCODE -ne 0) { throw "Windows x64 发布失败" }

$executable = Join-Path $publishPath "HMI.Server.exe"
$indexFile = Join-Path $publishPath "wwwroot/index.html"
if (-not (Test-Path $executable)) { throw "发布产物缺少 HMI.Server.exe" }
if (-not (Test-Path $indexFile)) { throw "发布产物缺少前端页面 wwwroot/index.html" }

$launcher = @'
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$baseUrl = "http://127.0.0.1:5080"
$stdout = Join-Path $env:TEMP "hmistudio-server.log"
$stderr = Join-Path $env:TEMP "hmistudio-server-error.log"
$exe = Join-Path $root "HMI.Server.exe"

if (-not (Test-Path $exe)) { throw "未找到 HMI.Server.exe，请重新解压完整发布包。" }
$existing = $null
try { $existing = Invoke-RestMethod "$baseUrl/api/health" -TimeoutSec 2 } catch {}
if ($existing -and $existing.status -eq "ok") {
    Write-Host "HMIStudio 已在运行：$baseUrl" -ForegroundColor Yellow
    Start-Process $baseUrl
    exit 0
}

$env:HMISTUDIO_URLS = $baseUrl
$server = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
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
    throw "HMIStudio 启动失败。日志：$stdout"
}
Write-Host "HMIStudio 已就绪：$baseUrl" -ForegroundColor Green
Write-Host "日志：$stdout"
Write-Host "停止服务：taskkill /PID $($server.Id) /T /F"
Start-Process $baseUrl
'@
Set-Content -Path (Join-Path $publishPath "Start-HMIStudio.ps1") -Value $launcher -Encoding utf8
@'
HMIStudio Windows x64 - Portable Release

运行要求：
- Windows 10/11 x64
- 无需单独安装 .NET Runtime/SDK 或 Node.js

启动：
1. 将整个 ZIP 文件解压到可写入的本地文件夹。
2. 在解压目录运行 Start-HMIStudio.ps1。
3. 浏览器将打开 http://127.0.0.1:5080。

停止：
在启动脚本输出的 PowerShell 窗口中使用显示的 taskkill 命令，或通过任务管理器结束 HMI.Server.exe。

工程和 SQLite 报警历史保存在本目录的 data 文件夹中。请备份 data 文件夹以保留数据。
本版本仅使用模拟 PLC，不要将服务暴露到不可信网络。
'@ | Set-Content -Path (Join-Path $publishPath "README-PORTABLE.txt") -Encoding utf8

Write-Host "[3/4] 打包 ZIP..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path $zipPath)) { throw "ZIP 打包失败" }

Write-Host "[4/4] 发布完成" -ForegroundColor Green
Write-Host "文件夹：$publishPath"
Write-Host "压缩包：$zipPath"
