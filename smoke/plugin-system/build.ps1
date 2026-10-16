<##############################################################################
# 插件体系冒烟资产一键构建与运行（PRD §5 M1~M3 复现入口）
#
#   1) 构建 framework 冒烟插件包（含二分定位变体）到 artifacts/
#   2) 跑 framework-harness 三种模式（unload / token / unload+timer）并汇总结果
#   3) 构建 codex 宿主冒烟插件并铺到 LubanAgentCodex 运行目录
#
# 用法：
#   pwsh -File build.ps1                 # 全流程
#   pwsh -File build.ps1 -SkipCodex      # 只跑 framework 侧
#   pwsh -File build.ps1 -Configuration Release
##############################################################################
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$SkipCodex
)

$ErrorActionPreference = 'Stop'

$root        = $PSScriptRoot
$artifacts   = Join-Path $root 'artifacts'
$pluginsRoot = Join-Path $artifacts 'plugins'
$timerRoot   = Join-Path $artifacts 'plugins-timer'
$codexRoot   = Join-Path $artifacts 'codex-plugins'

function Write-Step([string]$text)
{
    Write-Host ''
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Invoke-Dotnet([string[]]$arguments, [string]$what)
{
    Write-Host "> dotnet $($arguments -join ' ')"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$what 失败（dotnet 退出码 $LASTEXITCODE）" }
}

# ── 1. framework 冒烟插件包 ────────────────────────────────────────────────
function Build-FrameworkPlugin([string]$projectDir, [string]$packageRoot)
{
    $project = Join-Path $projectDir '*.csproj'
    Invoke-Dotnet @('build', (Get-Item $project).FullName, '-c', $Configuration, '--nologo') '构建冒烟插件'

    $output = Join-Path $projectDir "bin\$Configuration\net10.0"
    $lib    = Join-Path $packageRoot 'lib'

    if (Test-Path $packageRoot) { Remove-Item $packageRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $lib | Out-Null

    # 契约程序集（LuBan.*）由插件 ALC 强制共享、不随包分发，故 lib 只放插件自身
    Copy-Item (Join-Path $output 'HelloPlugin.dll')       $lib -Force
    Copy-Item (Join-Path $output 'HelloPlugin.deps.json') $lib -Force
    Copy-Item (Join-Path $output 'plugin.json')           $packageRoot -Force

    Write-Host "已生成插件包：$packageRoot"
}

Write-Step "1/3 构建 framework 冒烟插件包"
Build-FrameworkPlugin (Join-Path $root 'framework-plugins\HelloPlugin')      (Join-Path $pluginsRoot 'com.demo.hello')
Build-FrameworkPlugin (Join-Path $root 'framework-plugins\HelloPluginTimer') (Join-Path $timerRoot   'com.demo.hello')

# ── 2. framework-harness ──────────────────────────────────────────────────
Write-Step "2/3 运行 framework-harness"
$harness = Join-Path $root 'framework-harness\Harness.csproj'
Invoke-Dotnet @('build', $harness, '-c', $Configuration, '--nologo') '构建 harness'

$failures = 0
$scenarios = @(
    @{ Name = '正向：启用→停用→断言 ALC 真回收';   Mode = 'unload'; Root = $pluginsRoot },
    @{ Name = '反向：宿主保留 UI 委托→断言降级';    Mode = 'token';  Root = $pluginsRoot },
    @{ Name = '二分定位：定时器变体走正向模式';      Mode = 'unload'; Root = $timerRoot   }
)

foreach ($scenario in $scenarios)
{
    Write-Step $scenario.Name
    & dotnet run --project $harness -c $Configuration --no-build -- $scenario.Mode $scenario.Root
    if ($LASTEXITCODE -ne 0) { $failures++; Write-Host "本场景失败" -ForegroundColor Red }
}

# ── 3. codex 宿主冒烟插件 ─────────────────────────────────────────────────
if (-not $SkipCodex)
{
    Write-Step "3/3 构建 codex 冒烟插件并铺到运行目录"

    $codexProject = Join-Path $root 'codex-plugin\CodexSmokePlugin.csproj'
    Invoke-Dotnet @('build', $codexProject, '-c', $Configuration, '--nologo') '构建 codex 冒烟插件'

    $output      = Join-Path (Join-Path $root 'codex-plugin') "bin\$Configuration\net10.0"
    $packageRoot = Join-Path $codexRoot 'com.demo.codex'
    $deployRoot  = Join-Path $root "..\..\LubanAgentCodex\bin\$Configuration\net10.0\plugins\com.demo.codex"

    if (Test-Path $packageRoot) { Remove-Item $packageRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'lib') | Out-Null

    Copy-Item (Join-Path $output 'CodexSmokePlugin.dll')       (Join-Path $packageRoot 'lib') -Force
    Copy-Item (Join-Path $output 'CodexSmokePlugin.deps.json') (Join-Path $packageRoot 'lib') -Force
    Copy-Item (Join-Path $output 'plugin.json')                $packageRoot -Force
    Copy-Item (Join-Path $output 'skills')                     $packageRoot -Recurse -Force

    # 铺到 codex 运行目录（宿主按 plugins/<plugin-id>/ 扫描；重启宿主后生效）
    if (Test-Path $deployRoot) { Remove-Item $deployRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path $deployRoot) | Out-Null
    Copy-Item $packageRoot $deployRoot -Recurse -Force

    Write-Host "已铺到运行目录：$deployRoot"
    Write-Host '（重启 LubanAgentCodex 后，按 README §3 手工回归）' -ForegroundColor Yellow
}

Write-Step '结果'
if ($failures -gt 0)
{
    Write-Host "framework-harness：$failures 个场景失败" -ForegroundColor Red
    exit 1
}

Write-Host 'framework-harness：全部场景通过' -ForegroundColor Green
exit 0