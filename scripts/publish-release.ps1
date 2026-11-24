param(
    [string]$Version = '0.7.0',
    [string]$Output = '',
    [string]$PythonPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $repo ('publish-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (Test-Path -LiteralPath $Output) { throw 'Output must be a new directory.' }

# Resolve the actual interpreter so the preflight and packaging use the same Python.
$candidates = if ($PythonPath) { $PythonPath } else { 'python', 'python3', 'py' }
$pythonExe = $null
foreach ($candidate in $candidates) {
    $command = Get-Command -Name $candidate -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { continue }
    $launcherArgs = @()
    if ([IO.Path]::GetFileNameWithoutExtension($command.Path) -eq 'py') { $launcherArgs = @('-3') }
    $interpreter = & $command.Path @launcherArgs -c 'import sys; print(sys.executable) if sys.version_info >= (3, 11) else sys.exit(1)'
    if ($LASTEXITCODE -eq 0 -and $interpreter -and (Test-Path -LiteralPath $interpreter -PathType Leaf)) {
        $pythonExe = $interpreter
        break
    }
}
if (-not $pythonExe) { throw 'Python 3.11+ is required. Install it or supply -PythonPath with the interpreter path.' }

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stageName = 'practicaltools-publish-' + [Guid]::NewGuid().ToString('N')
$stage = New-Item -ItemType Directory -Path (Join-Path $tempRoot $stageName)
try {
    foreach ($item in @(@('win-x64','publish-win-x64'), @('osx-arm64','publish-mac-arm64'), @('linux-x64','publish-linux-x64'))) {
        dotnet publish (Join-Path $repo 'PracticalTools.Avalonia/PracticalTools.Avalonia.csproj') -c Release -r $item[0] --self-contained true -o (Join-Path $stage.FullName $item[1]) -p:Version=$Version
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($item[0])" }
    }
    & $pythonExe (Join-Path $PSScriptRoot 'package-release.py') --source-root $stage.FullName --version $Version --output $Output
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
}
finally {
    $actual = (Resolve-Path -LiteralPath $stage.FullName).ProviderPath
    if ($actual -eq [IO.Path]::GetFullPath((Join-Path $tempRoot $stageName))) {
        Remove-Item -LiteralPath $actual -Recurse -Force
    }
}
