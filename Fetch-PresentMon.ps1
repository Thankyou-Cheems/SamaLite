param([string]$CacheDirectory = (Join-Path $PSScriptRoot '.local/dependencies/PresentMon'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$executable = Join-Path $CacheDirectory 'PresentMon-2.6.0-x64.exe'
$expected = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
if (-not (Test-Path -LiteralPath $executable)) {
    Invoke-WebRequest -Uri 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' -OutFile ($executable + '.download')
    if ((Get-FileHash -LiteralPath ($executable + '.download') -Algorithm SHA256).Hash -ne $expected) { throw 'PresentMon download digest mismatch' }
    Move-Item -LiteralPath ($executable + '.download') -Destination $executable
}
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $expected) { throw 'PresentMon cached executable digest mismatch' }
foreach ($name in @('LICENSE.txt', 'THIRD_PARTY.txt')) {
    $path = Join-Path $CacheDirectory $name
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri ('https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/' + $name) -OutFile $path }
}
Write-Output $CacheDirectory
