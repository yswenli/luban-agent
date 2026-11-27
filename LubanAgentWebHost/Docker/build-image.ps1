#Requires -Version 7.0
<#
.SYNOPSIS
    发布 LubanAgentWebHost（Linux 运行时）并构建容器镜像。

.DESCRIPTION
    分两步：
      1. dotnet publish 发布到 LubanAgentWebHost/bin/docker-publish/<Runtime>，
         该目录同时作为 docker build 的上下文；
      2. 把本目录的 .dockerignore 复制到发布目录根部（Docker 只认上下文根目录的
         .dockerignore），再执行 docker build。

    镜像标签默认取 LubanAgentWebHost.csproj 的 <Version>，并额外打一个 latest。

.PARAMETER Tag
    镜像标签，默认 luban-agent-webhost:<csproj 版本>。

.PARAMETER Runtime
    目标运行时，默认 linux-x64；ARM 宿主可传 linux-arm64。

.PARAMETER SkipPublish
    跳过 dotnet publish，直接用发布目录里现有产物构建（调试 Dockerfile 时用）。

.PARAMETER NoCache
    docker build --no-cache。

.EXAMPLE
    pwsh -File .\build-image.ps1
    pwsh -File .\build-image.ps1 -Tag luban-agent-webhost:dev -NoCache
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [string]$Configuration = 'Release',
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string]$Runtime = 'linux-x64',
    [switch]$SkipPublish,
    [switch]$NoCache
)

$ErrorActionPreference = 'Stop'

$dockerDir = $PSScriptRoot
$projectDir = Split-Path -Parent $dockerDir
$project = Join-Path $projectDir 'LubanAgentWebHost.csproj'
$publishDir = Join-Path $projectDir "bin\docker-publish\$Runtime"
$dockerignore = Join-Path $dockerDir '.dockerignore'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "找不到 docker 命令，请先安装并启动 Docker。"
}

# 版本号取 csproj，避免镜像标签与产品版本漂移
if (-not $Tag) {
    [xml]$csproj = Get-Content -LiteralPath $project -Raw
    $version = @($csproj.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw "无法从 $project 读取 <Version>，请用 -Tag 显式指定镜像标签。" }
    $Tag = "luban-agent-webhost:$version"
}

if (-not $SkipPublish) {
    # 复用旧目录会留下已删除的产物，导致镜像里混入过期文件
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    Write-Host "[docker] dotnet publish -> $publishDir"
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -o $publishDir `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）。" }
}

Copy-Item -LiteralPath $dockerignore -Destination (Join-Path $publishDir '.dockerignore') -Force

$buildArgs = @('build', '-f', (Join-Path $dockerDir 'Dockerfile'), '-t', $Tag)
if ($NoCache) { $buildArgs += '--no-cache' }
$buildArgs += $publishDir

Write-Host "[docker] docker $($buildArgs -join ' ')"
& docker @buildArgs
if ($LASTEXITCODE -ne 0) { throw "docker build 失败（退出码 $LASTEXITCODE）。" }

# 额外打 latest，compose 默认引用它；带仓库地址的标签不重复打
$repository = $Tag.Split(':')[0]
if ($repository -eq 'luban-agent-webhost') {
    docker tag $Tag 'luban-agent-webhost:latest'
}

$size = docker image inspect $Tag --format '{{.Size}}'
if ($LASTEXITCODE -eq 0 -and $size) {
    $mb = [Math]::Round([int64]$size / 1MB)
    Write-Host "[docker] 已生成 $Tag（$mb MB，未压缩）"
}

Write-Host @"

运行示例：
  先照 .env.example 建一份 .env（路径、模型下载地址、管理员密码都在里面），再：
  docker run -d --name luban-agent-webhost -p 18080:18080 `
      --env-file .env `
      -v luban-agent-data:/data $Tag

或使用同目录的 docker-compose.yml：
  docker compose up -d
"@