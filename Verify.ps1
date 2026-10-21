$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Push-Location $PSScriptRoot
try {
    dotnet build SamaLite.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $verificationDirectory = Join-Path $PSScriptRoot '.local/next-verification'
    dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/SamaLite.dll') --verify $verificationDirectory
    if ($LASTEXITCODE -ne 0) { Get-Content -Raw -Encoding utf8 (Join-Path $verificationDirectory 'model-results.json'); exit $LASTEXITCODE }
    Get-Content -Raw -Encoding utf8 (Join-Path $verificationDirectory 'model-results.json')
} finally { Pop-Location }
