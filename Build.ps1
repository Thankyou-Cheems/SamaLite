$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
$project = Join-Path $PSScriptRoot 'SamaLite.csproj'
dotnet restore $project -r win-x64 --locked-mode --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project -c Release -r win-x64 --self-contained false --no-restore -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $PSScriptRoot 'app') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$licenses = Join-Path $PSScriptRoot 'app\licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty\TuringSmartScreen\LICENSE.txt') -Destination (Join-Path $licenses 'TuringSmartScreen-MIT.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $licenses 'SamaLite-MIT.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $PSScriptRoot 'app')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'app')
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenses -Recurse -Force }
