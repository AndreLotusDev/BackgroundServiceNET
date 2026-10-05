<#
.SYNOPSIS  Starts the local stack: LocalStack + S3 browser (docker compose), then Identity, Worker and Web from publish/out.
           Builds first if the executables are missing. Stop with publish/down.ps1.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'out'
$logs = Join-Path $PSScriptRoot 'logs'
$pidFile = Join-Path $PSScriptRoot '.pids'
New-Item -ItemType Directory -Force $logs | Out-Null

if (Test-Path $pidFile) { & (Join-Path $PSScriptRoot 'down.ps1') -KeepContainers }

foreach ($svc in 'identity', 'worker', 'web') {
    if (-not (Test-Path (Join-Path $out "$svc/SteamItems.$((Get-Culture).TextInfo.ToTitleCase($svc)).exe"))) {
        & (Join-Path $PSScriptRoot 'build.ps1'); break
    }
}

# Identity and Web need HTTPS; Kestrel uses the ASP.NET dev certificate.
dotnet dev-certs https --trust | Out-Null

Write-Host '==> Starting LocalStack (S3 + SQS) and s3manager' -ForegroundColor Cyan
docker compose -f (Join-Path $root 'docker-compose.yml') up -d --wait localstack
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed (is Docker running?)' }
docker compose -f (Join-Path $root 'docker-compose.yml') up -d s3manager | Out-Null

# Development environment: applies migrations, seeds users, loads the localhost settings.
$services = @(
    @{ Name = 'identity'; Exe = 'SteamItems.Identity.exe'; Urls = 'https://localhost:5001' },
    @{ Name = 'worker';   Exe = 'SteamItems.Worker.exe';   Urls = 'http://localhost:5290' },
    @{ Name = 'web';      Exe = 'SteamItems.Web.exe';      Urls = 'https://localhost:7281;http://localhost:5017' }
)
$pids = @()
foreach ($s in $services) {
    $dir = Join-Path $out $s.Name
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $s.Urls
    Write-Host "==> Starting $($s.Name) on $($s.Urls)" -ForegroundColor Cyan
    $p = Start-Process -FilePath (Join-Path $dir $s.Exe) -WorkingDirectory $dir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logs "$($s.Name).log") -RedirectStandardError (Join-Path $logs "$($s.Name).err.log")
    $pids += $p.Id
    if ($s.Name -eq 'identity') { Start-Sleep -Seconds 10 }  # Web discovers Identity at startup
}
Remove-Item Env:ASPNETCORE_URLS
$pids | Set-Content $pidFile

Write-Host @'

Stack is up:
  Web          https://localhost:7281   (login: alice / Pass123$)
  Identity     https://localhost:5001
  Worker       http://localhost:5290
  S3 browser   http://localhost:8080
  LocalStack   http://localhost:4566
Logs: publish/logs/   Stop: publish/down.ps1
'@ -ForegroundColor Green
