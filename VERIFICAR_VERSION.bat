@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo AMP ACCESSIBLE 2.41.58 - VERIFICAR VERSION
echo ============================================================
echo.
set "EXE="
if exist "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" set "EXE=%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe"
if not defined EXE if exist "%~dp0PUBLICACION\AmpAccessible.exe" set "EXE=%~dp0PUBLICACION\AmpAccessible.exe"
if not defined EXE if exist "%LocalAppData%\Programs\GDM Amp Accessible\AmpAccessible.exe" set "EXE=%LocalAppData%\Programs\GDM Amp Accessible\AmpAccessible.exe"
if not defined EXE (
  echo Todavia no se encontro AmpAccessible.exe compilado o instalado.
  echo Version esperada del proyecto: 2.41.58
  echo.
  pause
  exit /b 1
)
echo Ejecutable encontrado:
echo %EXE%
echo.
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%EXE%').VersionInfo.FileVersion"`) do set "FILEVER=%%V"
echo Version de archivo: %FILEVER%
echo Version esperada: 2.41.58.0
echo.
echo Al abrir la aplicacion, el titulo debe decir: Amp Accessible 2.41.58
echo En Alt+D, la primera linea debe decir: Amp Accessible 2.41.58 - diagnostico accesible
echo.
pause
