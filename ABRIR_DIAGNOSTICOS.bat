@echo off
rem Amp Accessible 2.41.58
set "RUTA=%LOCALAPPDATA%\GDM Amp Accessible\Diagnosticos"
if not exist "%RUTA%" (
  echo Todavia no existe la carpeta de diagnosticos.
  echo Se creara automaticamente al detener el audio o si se detecta un cuelgue.
  pause
  exit /b 0
)
start "" "%RUTA%"
