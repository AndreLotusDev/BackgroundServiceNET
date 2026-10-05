<#
.SYNOPSIS  Stops the services started by up.ps1 and the docker compose containers.
#>
param([switch]$KeepContainers)
$root = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $PSScriptRoot '.pids'
if (Test-Path $pidFile) {
    Get-Content $pidFile | ForEach-Object { Stop-Process -Id $_ -ErrorAction SilentlyContinue }
    Remove-Item $pidFile
}
if (-not $KeepContainers) {
    docker compose -f (Join-Path $root 'docker-compose.yml') down
}
Write-Host 'Stack stopped.' -ForegroundColor Green
