@echo off
setlocal

dotnet fsi build.fsx -- %*
exit /b %errorlevel%
