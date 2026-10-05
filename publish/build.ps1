<#
.SYNOPSIS  Publishes every service as a self-contained single-file executable (no loose DLLs; appsettings*.json and wwwroot stay beside it) into publish/out/<service>/.
.PARAMETER Runtime  RID to build for (default win-x64). Use linux-x64 for a Linux binary.
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'out'
$services = @('Identity', 'Worker', 'Web')

foreach ($svc in $services) {
    $dest = Join-Path $out $svc.ToLower()
    Write-Host "==> Publishing SteamItems.$svc ($Runtime) -> $dest" -ForegroundColor Cyan
    dotnet publish (Join-Path $root "src/SteamItems.$svc/SteamItems.$svc.csproj") `
        -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $dest
    if ($LASTEXITCODE -ne 0) { throw "Publish of $svc failed" }
    # Local databases are created next to the exe at runtime; never ship dev ones.
    Get-ChildItem $dest -Filter '*.db*' -ErrorAction SilentlyContinue | Remove-Item -Force
    Remove-Item (Join-Path $dest 'web.config') -ErrorAction SilentlyContinue  # IIS-only

    # Single-file check: one executable, no loose assemblies (config and wwwroot stay beside it).
    $ext = if ($Runtime -like 'win-*') { '.exe' } else { '' }
    if (-not (Test-Path (Join-Path $dest "SteamItems.$svc$ext"))) { throw "SteamItems.$svc$ext missing in $dest" }
    $loose = Get-ChildItem $dest -Filter '*.dll'
    if ($loose) { throw "Not single-file, loose assemblies in ${dest}: $($loose.Name -join ', ')" }
}
Write-Host "Done. Run publish/up.ps1 to start the local stack." -ForegroundColor Green
