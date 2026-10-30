param([string]$Version = '0.7.0', [string]$Output = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $repo ('publish-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (Test-Path -LiteralPath $Output) { throw 'Output must be a new directory.' }
foreach ($item in @(@('win-x64','publish-win-x64'), @('osx-arm64','publish-mac-arm64'), @('linux-x64','publish-linux-x64'))) {
    dotnet publish (Join-Path $repo 'PracticalTools.Avalonia/PracticalTools.Avalonia.csproj') -c Release -r $item[0] --self-contained true -o (Join-Path $repo $item[1]) -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($item[0])" }
}
python (Join-Path $PSScriptRoot 'package-release.py') --version $Version --output $Output
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
