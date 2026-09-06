@echo off
rem Amp Accessible 2.41.58
setlocal
cd /d "%~dp0"

if exist "%~dp0PUBLICACION\AmpAccessible.exe" (
  start "" "%~dp0PUBLICACION\AmpAccessible.exe"
  exit /b 0
)

if exist "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" (
  start "" "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe"
  exit /b 0
)

echo No se encontro la aplicacion compilada.
echo Ejecute primero COMPILAR.bat.
pause
