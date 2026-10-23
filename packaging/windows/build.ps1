# Builds the Windows installer and portable zip from a self-contained win-x64 publish folder.
# Usage (from the repository root): pwsh packaging/windows/build.ps1 -Version 1.2.3 -PublishDir <dir> -OutputDir <dir>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $OutputDir
)
$ErrorActionPreference = 'Stop'

$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (-not (Test-Path $candidate)) {
        choco install innosetup --no-progress -y | Out-Host
    }
    $iscc = Get-Item $candidate
}

& $iscc.Source /Qp "/DAppVersion=$Version" "/DPublishDir=$PublishDir" "/DOutputDir=$OutputDir" packaging/windows/lumen.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$zip = Join-Path $OutputDir "Lumen-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip -Force
Get-ChildItem $OutputDir | Format-Table Name, Length
