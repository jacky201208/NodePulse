# build-all.ps1
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  NodePulse 全平台编译" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$projectDir = "E:\zhaoqingxi\桌面\NodePulse launcher\NodePulse"
Set-Location $projectDir

# 输出目录
$outputDir = Join-Path $projectDir "Publish"
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Path $outputDir | Out-Null

# 公共参数
$commonArgs = @(
    "-c", "Release",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

# 平台列表
$platforms = @(
    @{RID="win-x64";    Out="NodePulse-v1.0.0-win-x64.exe"},
    @{RID="win-arm64";  Out="NodePulse-v1.0.0-win-arm64.exe"},
    @{RID="linux-x64";  Out="NodePulse-v1.0.0-linux-x64"},
    @{RID="linux-arm64";Out="NodePulse-v1.0.0-linux-arm64"},
    @{RID="osx-x64";    Out="NodePulse-v1.0.0-macos-x64"},
    @{RID="osx-arm64";  Out="NodePulse-v1.0.0-macos-arm64"}
)

$total = $platforms.Count
$i = 0

foreach ($p in $platforms) {
    $i++
    $rid = $p.RID
    $outName = $p.Out

    Write-Host ""
    Write-Host "[$i/$total] 正在编译 $rid ..." -ForegroundColor Yellow

    # 执行发布
    dotnet publish -r $rid @commonArgs 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ $rid 编译失败" -ForegroundColor Red
        continue
    }

    # 找到产物
    $publishDir = Join-Path $projectDir "bin\Release\net8.0\$rid\publish"
    $exeFile = if ($rid -like "win-*") {
        Join-Path $publishDir "NodePulse.exe"
    } else {
        Join-Path $publishDir "NodePulse"
    }

    if (-not (Test-Path $exeFile)) {
        Write-Host "  ✗ 找不到产物：$exeFile" -ForegroundColor Red
        continue
    }

    # 复制并重命名
    Copy-Item $exeFile (Join-Path $outputDir $outName) -Force

    # 查看大小
    $size = [math]::Round((Get-Item (Join-Path $outputDir $outName)).Length / 1MB, 1)
    Write-Host "  ✓ $outName  ($size MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  全部完成！输出目录：$outputDir" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan