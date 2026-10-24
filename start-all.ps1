# start-all.ps1 — Kill old instances and start both services

Write-Host "Stopping any running instances..." -ForegroundColor Yellow

@(5296, 5100) | ForEach-Object {
    $port = $_
    $pids = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess
    foreach ($p in $pids) { taskkill /F /PID $p 2>$null }
}
taskkill /F /IM Project_OLMS.exe 2>$null
taskkill /F /IM OCR_Frontend.exe 2>$null

Start-Sleep -Seconds 1

$backendDir = Join-Path $PSScriptRoot "Project_OLMS\Project_OLMS"
$frontendDir = Join-Path $PSScriptRoot "OCR_Frontend\OCR_Frontend"

Write-Host "Starting Backend (port 5296)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit","-Command","cd '$backendDir'; dotnet run" -WindowStyle Normal

Start-Sleep -Seconds 3

Write-Host "Starting Frontend (port 5100)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit","-Command","cd '$frontendDir'; dotnet run" -WindowStyle Normal

Write-Host ""
Write-Host "Done! Open: http://localhost:5100" -ForegroundColor Green
