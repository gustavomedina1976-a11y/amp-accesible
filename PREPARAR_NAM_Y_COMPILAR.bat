@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo GDM AMP ACCESSIBLE 2.41.58 - NAM Y COMPILACION EN UN PASO
echo ============================================================
echo.
echo Primero se preparara NeuralAudioCAPI.dll y despues Amp Accessible.
echo Si algo falla, se abrira DIAGNOSTICO_MOTOR_NAM.txt.
echo.

call "%~dp0REPARAR_MOTOR_NAM.bat"
if errorlevel 1 (
  echo.
  echo No se ejecutara la compilacion de Amp Accessible porque NAM fallo.
  pause
  exit /b 1
)

call "%~dp0COMPILAR.bat"
exit /b %errorlevel%
