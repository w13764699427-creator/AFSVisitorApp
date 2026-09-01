# VisitorApp Windows one-click publish script
# Usage: powershell -ExecutionPolicy Bypass -File .\publish-win.ps1
# Output: publish\win-x64-sc (self-contained, no .NET runtime needed on client)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

# Clear proxy env vars (local proxy breaks NuGet restore on this machine)
$env:HTTP_PROXY = $null
$env:HTTPS_PROXY = $null
$env:NO_PROXY = "*"

# Stop running app first to avoid file locking
Get-Process VisitorApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Publishing (self-contained, auto retry on network flakiness)..." -ForegroundColor Cyan
for ($i = 1; $i -le 4; $i++) {
    dotnet publish VisitorApp.csproj `
        -f net10.0-windows10.0.19041.0 `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishReadyToRun=false `
        "-p:TargetFrameworks=net10.0-windows10.0.19041.0" `
        -o publish\win-x64-sc
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "Attempt $i failed, retry in 3s..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed, see errors above." -ForegroundColor Red
    exit 1
}

$size = "{0:N0} MB" -f ((Get-ChildItem publish\win-x64-sc -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host ""
Write-Host "Done: publish\win-x64-sc\VisitorApp.exe ($size)" -ForegroundColor Green
Write-Host "Copy the whole win-x64-sc folder to the client machine. (ID card reader driver still required.)" -ForegroundColor Green
