@echo off
rem One-button build. Usage: tools\build\build.bat [-Target All|Windows|Android|PlayStore|TestSession] [-Retries N]
rem Runs build.ps1 with the script policy bypassed for this one process only.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
