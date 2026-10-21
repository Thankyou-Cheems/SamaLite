#requires -Version 7.0
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SamaLite.csproj') -Raw -Encoding utf8
$version = [string]$project.Project.PropertyGroup.Version
$label = "SamaLite-$version-win-x64-portable"
$staging = Join-Path $OutputDirectory ("staging-" + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $staging 'SamaLite'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Path (Join-Path $payload 'data') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'data/.keep'), '', [Text.UTF8Encoding]::new($false))
$manifest = [ordered]@{ Version = $version; Architecture = 'win-x64'; FrameworkDependent = $true; ExtractAtRuntime = $false
    DataDirectory = 'data'; Prerequisites = @('.NET 10 Windows Desktop Runtime x64', 'Existing PawnIO driver for CPU/DIMM telemetry')
    Files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ Path = [IO.Path]::GetRelativePath($payload, $_.FullName); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }) }
[IO.File]::WriteAllText((Join-Path $payload 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$archive = Join-Path $OutputDirectory "$label.zip"
Compress-Archive -LiteralPath $payload -DestinationPath $archive -CompressionLevel Optimal -Force
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
[ordered]@{ Archive = [IO.Path]::GetFullPath($archive); Payload = [IO.Path]::GetFullPath($payload)
    Files = $files.Count; UnpackedMiB = ($files | Measure-Object Length -Sum).Sum / 1MB
    ArchiveMiB = (Get-Item -LiteralPath $archive).Length / 1MB; SHA256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash } | ConvertTo-Json
