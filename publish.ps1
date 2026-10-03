# Window Tiling Manager - 배포용 실행 파일 만들기
# 결과: release\WindowTilingManager\WindowTilingManager.exe (.NET 설치 없이 실행됨)
#       release\WindowTilingManager-<버전>-win-x64.zip      (배포용 압축 파일)
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$version = ([xml](Get-Content -Encoding UTF8 'WindowTilingManager.csproj')).Project.PropertyGroup.Version | Select-Object -First 1
$out = Join-Path 'release' 'WindowTilingManager'
$zip = Join-Path 'release' "WindowTilingManager-$version-win-x64.zip"

Write-Host "버전 $version 배포 파일을 만듭니다..." -ForegroundColor Cyan
if (Test-Path 'release') { Remove-Item -Recurse -Force 'release' }

dotnet publish WindowTilingManager.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $out
if ($LASTEXITCODE -ne 0) { Write-Host '빌드에 실패했습니다.' -ForegroundColor Red; exit 1 }

Copy-Item 'README.md', 'LICENSE' -Destination $out
Get-ChildItem -Filter '*.md' | Where-Object Name -ne 'README.md' | Copy-Item -Destination $out
Compress-Archive -Path $out -DestinationPath $zip -Force

Write-Host ''
Write-Host "완료:" -ForegroundColor Green
Write-Host "  실행 파일: $out\WindowTilingManager.exe"
Write-Host "  배포 zip : $zip"
