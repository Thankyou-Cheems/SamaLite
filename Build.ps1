param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'app'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
$project = Join-Path $PSScriptRoot 'SamaLite.csproj'
dotnet restore $project -r win-x64 --locked-mode --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project -c Release -r win-x64 --self-contained false --no-restore -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -p:IncludeAllContentForSelfExtract=false -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$licenses = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty\TuringSmartScreen\LICENSE.txt') -Destination (Join-Path $licenses 'TuringSmartScreen-MIT.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $licenses 'SamaLite-MIT.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Startup.ps1') -Destination $OutputDirectory
$imageOutput = Join-Path $OutputDirectory 'docs/images'
New-Item -ItemType Directory -Path $imageOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/images/circular-layout-sample.png') -Destination $imageOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/images/game-layout-sample.png') -Destination $imageOutput
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenses -Recurse -Force }
$presentMonCache = & (Join-Path $PSScriptRoot 'Fetch-PresentMon.ps1')
$presentMonOutput = Join-Path $OutputDirectory 'tools/PresentMon'
New-Item -ItemType Directory -Path $presentMonOutput -Force | Out-Null
Get-ChildItem -LiteralPath $presentMonCache -File | Where-Object Name -In @('PresentMon-2.6.0-x64.exe', 'LICENSE.txt', 'THIRD_PARTY.txt') | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $presentMonOutput -Force }
