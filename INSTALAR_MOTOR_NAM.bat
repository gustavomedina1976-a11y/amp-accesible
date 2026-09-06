@echo off
rem Amp Accessible 2.41.58
cd /d "%~dp0"
call "%~dp0REPARAR_MOTOR_NAM.bat" %*
exit /b %errorlevel%
