param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\VideoAudioExtractor\VideoAudioExtractor.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish'
$installerDir = Join-Path $repoRoot 'artifacts\installer'

& (Join-Path $PSScriptRoot 'setup-dependencies.ps1')

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (-not $SkipInstaller -and (Test-Path -LiteralPath $installerDir)) {
    Get-ChildItem -LiteralPath $installerDir -Filter '*.exe' -File | Remove-Item -Force
}

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败，退出码 $LASTEXITCODE" }
dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败，退出码 $LASTEXITCODE" }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

if (-not $SkipInstaller) {
    $isccCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw '找不到 Inno Setup 6 编译器 ISCC.exe。' }
    & $iscc (Join-Path $repoRoot 'installer\VideoAudioExtractor.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败，退出码 $LASTEXITCODE" }
}
