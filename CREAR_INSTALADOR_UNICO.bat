@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem Exige que todo el paquete haya sido extraido del ZIP.
if not exist "%~dp0REPARAR_MOTOR_NAM.bat" goto :paquete_no_extraido
if not exist "%~dp0COMPILAR_AUTOCONTENIDO.bat" goto :paquete_no_extraido
if not exist "%~dp0GDMAmpAccessible.csproj" goto :paquete_no_extraido
if not exist "%~dp0Installer\AmpAccessible.iss" goto :paquete_no_extraido
title Amp Accessible 2.41.58 - Crear instalador unico

set "ROOT=%~dp0"
set "LOG=%ROOT%DIAGNOSTICO_INSTALADOR.txt"
set "FINAL=%ROOT%Amp_Accessible_Setup.exe"
set "INNO_OUT=%ROOT%Installer\Salida\Amp_Accessible_Setup.exe"

>"%LOG%" echo ============================================================
>>"%LOG%" echo AMP ACCESSIBLE 2.41.58 - DIAGNOSTICO INSTALADOR
>>"%LOG%" echo Fecha: %date% %time%
>>"%LOG%" echo Carpeta: %ROOT%
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.

echo ============================================================
echo AMP ACCESSIBLE 2.41.58 - CREAR INSTALADOR UNICO
echo ============================================================
echo.
echo Al terminar correctamente aparecera ESTE archivo:
echo %FINAL%
echo.
echo No necesita buscarlo dentro de subcarpetas.
echo.

if exist "%FINAL%" del /q "%FINAL%" >nul 2>nul

call "%~dp0REPARAR_MOTOR_NAM.bat" /AUTO
if errorlevel 1 (
  >>"%LOG%" echo ERROR: REPARAR_MOTOR_NAM.bat devolvio error.
  goto :fail
)
>>"%LOG%" echo Motor NAM: OK

call "%~dp0COMPILAR_AUTOCONTENIDO.bat" /AUTO
if errorlevel 1 (
  >>"%LOG%" echo ERROR: COMPILAR_AUTOCONTENIDO.bat devolvio error.
  goto :fail
)

if not exist "%ROOT%PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" (
  >>"%LOG%" echo ERROR: Falta PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe
  goto :fail
)
if not exist "%ROOT%PUBLICACION_AUTOCONTENIDA\NeuralAudioCAPI.dll" (
  >>"%LOG%" echo ERROR: Falta PUBLICACION_AUTOCONTENIDA\NeuralAudioCAPI.dll
  goto :fail
)
>>"%LOG%" echo Publicacion autocontenida: OK

call :resolve_inno
if defined ISCC goto :have_inno

where winget >nul 2>nul
if errorlevel 1 (
  >>"%LOG%" echo ERROR: Inno Setup no encontrado y winget no esta disponible.
  echo ERROR: No encuentro Inno Setup 6/7 ni winget.
  goto :fail
)

echo Inno Setup no esta instalado. Se intentara instalar Inno Setup 7.
>>"%LOG%" echo Inno Setup no encontrado. Intentando winget JRSoftware.InnoSetup.7...
winget install --id JRSoftware.InnoSetup.7 -e -s winget --accept-package-agreements --accept-source-agreements -i >>"%LOG%" 2>&1
if errorlevel 1 (
  >>"%LOG%" echo AVISO: fallo Inno Setup 7. Intentando Inno Setup 6...
  winget install --id JRSoftware.InnoSetup -e -s winget --accept-package-agreements --accept-source-agreements -i >>"%LOG%" 2>&1
)

call :resolve_inno
if not defined ISCC (
  echo ERROR: Inno Setup se intento instalar pero no encuentro ISCC.exe.
  >>"%LOG%" echo ERROR: ISCC.exe sigue sin encontrarse tras winget.
  goto :fail
)

:have_inno
echo Compilador de instalador encontrado:
echo %ISCC%
>>"%LOG%" echo ISCC: %ISCC%

if exist "%ROOT%Installer\Salida" rmdir /s /q "%ROOT%Installer\Salida" >>"%LOG%" 2>&1
mkdir "%ROOT%Installer\Salida" >>"%LOG%" 2>&1

echo Generando Amp_Accessible_Setup.exe...
"%ISCC%" "%ROOT%Installer\AmpAccessible.iss" >>"%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: Inno Setup encontro un error al compilar el instalador.
  goto :fail
)

if not exist "%INNO_OUT%" (
  echo ERROR: Inno Setup termino pero no aparecio el instalador esperado.
  >>"%LOG%" echo ERROR: No existe %INNO_OUT%
  goto :fail
)

copy /y "%INNO_OUT%" "%FINAL%" >>"%LOG%" 2>&1
if errorlevel 1 goto :fail
if not exist "%FINAL%" goto :fail

for %%F in ("%FINAL%") do >>"%LOG%" echo Instalador final: %%~fF - %%~zF bytes
>>"%LOG%" echo RESULTADO: INSTALADOR CREADO CORRECTAMENTE.

echo.
echo ============================================================
echo INSTALADOR CREADO CORRECTAMENTE
echo ============================================================
echo.
echo Archivo:
echo %FINAL%
echo.
echo Este es el unico EXE que debe usar para instalar,
echo reparar o actualizar Amp Accessible.
echo.
explorer.exe /select,"%FINAL%"
pause
exit /b 0

:resolve_inno
set "ISCC="
for %%P in (
  "%ProgramFiles%\Inno Setup 7\ISCC.exe"
  "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
  "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe"
  "%ProgramFiles%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
) do (
  if not defined ISCC if exist "%%~P" set "ISCC=%%~P"
)
if defined ISCC exit /b 0

for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do (
  if not defined ISCC set "ISCC=%%I"
)
if defined ISCC exit /b 0

rem Ultimo recurso: busca ISCC.exe solo en las carpetas habituales.
for %%D in ("%ProgramFiles%" "%ProgramFiles(x86)%" "%LocalAppData%\Programs") do (
  if not defined ISCC if exist "%%~D" (
    for /f "delims=" %%I in ('where /r "%%~D" ISCC.exe 2^>nul') do (
      if not defined ISCC set "ISCC=%%I"
    )
  )
)
exit /b 0

:fail
>>"%LOG%" echo.
>>"%LOG%" echo RESULTADO: ERROR.
echo.
echo ============================================================
echo NO SE PUDO CREAR EL INSTALADOR
echo ============================================================
echo.
echo Se abrira DIAGNOSTICO_INSTALADOR.txt.
echo Ese archivo contiene el punto exacto donde fallo.
echo.
start "" notepad.exe "%LOG%"
pause
exit /b 1


:paquete_no_extraido
cls
echo ============================================================
echo AMP ACCESSIBLE - EL ZIP NO ESTA EXTRAIDO COMPLETO
echo ============================================================
echo.
echo No encuentro todos los archivos del proyecto junto a este BAT.
echo Extraiga el ZIP completo antes de generar el instalador unico.
echo.
echo En el Explorador: seleccione el ZIP, use Extraer todo, entre a la carpeta extraida
echo y recien entonces ejecute CREAR_INSTALADOR_UNICO.bat.
echo.
pause
exit /b 2
