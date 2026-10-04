$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$ffmpegDir = Join-Path $repoRoot 'src\VideoAudioExtractor\ffmpeg'
$ffmpegExe = Join-Path $ffmpegDir 'ffmpeg.exe'
$ffprobeExe = Join-Path $ffmpegDir 'ffprobe.exe'

if ((Test-Path -LiteralPath $ffmpegExe) -and (Test-Path -LiteralPath $ffprobeExe)) {
    Write-Host 'FFmpeg 组件已存在，跳过下载。'
    return
}

$downloadDir = Join-Path $repoRoot 'tools\downloads'
$extractDir = Join-Path $repoRoot 'tools\ffmpeg-extract'
$archivePath = Join-Path $downloadDir 'ffmpeg-release-essentials.zip'

New-Item -ItemType Directory -Force -Path $downloadDir, $ffmpegDir | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host '正在下载 FFmpeg Windows essentials build…'
    Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $archivePath
}

if (Test-Path -LiteralPath $extractDir) {
    $resolvedExtract = [IO.Path]::GetFullPath($extractDir)
    $resolvedTools = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tools'))
    if (-not $resolvedExtract.StartsWith($resolvedTools, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 tools 目录之外的路径：$resolvedExtract"
    }
    Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDir -Force
$distribution = Get-ChildItem -LiteralPath $extractDir -Directory | Select-Object -First 1
if (-not $distribution) { throw 'FFmpeg 压缩包结构无效。' }

$sourceFfmpeg = Join-Path $distribution.FullName 'bin\ffmpeg.exe'
$sourceFfprobe = Join-Path $distribution.FullName 'bin\ffprobe.exe'
if (-not (Test-Path -LiteralPath $sourceFfmpeg) -or -not (Test-Path -LiteralPath $sourceFfprobe)) {
    throw 'FFmpeg 压缩包中缺少 ffmpeg.exe 或 ffprobe.exe。'
}

Copy-Item -LiteralPath $sourceFfmpeg -Destination $ffmpegExe -Force
Copy-Item -LiteralPath $sourceFfprobe -Destination $ffprobeExe -Force

$license = Join-Path $distribution.FullName 'LICENSE'
$readme = Join-Path $distribution.FullName 'README.txt'
if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $ffmpegDir 'LICENSE.txt') -Force }
if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $ffmpegDir 'README.txt') -Force }

Write-Host "FFmpeg 组件已准备到：$ffmpegDir"
