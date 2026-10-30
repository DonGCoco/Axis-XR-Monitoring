@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo   Axis XR - Windows Proxy
echo ==========================================

where py >nul 2>nul
if %errorlevel%==0 (
  py -3 axis_proxy_multi.py --lan
  goto :end
)

where python >nul 2>nul
if %errorlevel%==0 (
  python axis_proxy_multi.py --lan
  goto :end
)

echo Python 3 was not found.
echo Install Python 3 from python.org, then run this file again.
pause

:end
endlocal
