$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Axis XR - Windows Lab Network Check" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host "Please run PowerShell as Administrator, then run this script again." -ForegroundColor Yellow
    exit 1
}

$ruleName = "Axis XR Proxy TCP 5000"
$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if (-not $rule) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5000 -Profile Private | Out-Null
    Write-Host "Firewall rule created for TCP 5000 on Private networks." -ForegroundColor Green
} else {
    Write-Host "Firewall rule already exists." -ForegroundColor Green
}

Write-Host ""
Write-Host "IPv4 interfaces:" -ForegroundColor Cyan
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" } | Sort-Object InterfaceAlias | Format-Table InterfaceAlias, IPAddress, PrefixLength -AutoSize

Write-Host ""
Write-Host "Checking Axis cameras over Ethernet..." -ForegroundColor Cyan
$cameras = @(
    @{ Name = "CAM_001"; IP = "169.254.96.234" },
    @{ Name = "CAM_002"; IP = "169.254.52.210" }
)
foreach ($camera in $cameras) {
    $ok = Test-Connection -ComputerName $camera.IP -Count 1 -Quiet -ErrorAction SilentlyContinue
    if ($ok) {
        Write-Host "$($camera.Name) $($camera.IP): reachable" -ForegroundColor Green
    } else {
        Write-Host "$($camera.Name) $($camera.IP): NOT reachable" -ForegroundColor Red
    }
}

Write-Host ""
$hotspot = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -eq "192.168.137.1" }
if ($hotspot) {
    Write-Host "Windows Mobile Hotspot IP detected: 192.168.137.1" -ForegroundColor Green
    Write-Host "Quest app proxy URL: http://192.168.137.1:5000" -ForegroundColor Green
} else {
    Write-Host "192.168.137.1 is not active yet." -ForegroundColor Yellow
    Write-Host "Turn on Settings > Network & internet > Mobile hotspot, connect Quest, then run this script again." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Next: run start_proxy_windows.bat in this folder." -ForegroundColor Cyan
