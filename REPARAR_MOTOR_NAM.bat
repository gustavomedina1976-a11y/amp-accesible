@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "LOG=%~dp0DIAGNOSTICO_MOTOR_NAM.txt"
set "WORK=%TEMP%\GDM_NAM_2171"
set "SRC=%WORK%\src"
set "BLD=%WORK%\b"

> "%LOG%" echo ============================================================
>>"%LOG%" echo GDM Amp Accessible 2.41.58 - Reparacion Motor NAM
>>"%LOG%" echo Fecha: %date% %time%
>>"%LOG%" echo Carpeta del programa: %CD%
>>"%LOG%" echo Carpeta corta de trabajo: %WORK%
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.

echo ============================================================
echo GDM AMP ACCESSIBLE 2.41.58 - REPARAR MOTOR NAM
echo ============================================================
echo.
echo Esta version corrige el error de Windows/Git "Filename too long".
echo El motor NAM se compilara en una ruta corta temporal:
echo %WORK%
echo.
echo La ventana NO se cerrara sola si ocurre un error.
echo.

call :check git "Git"
if errorlevel 1 goto :fail
call :check cmake "CMake"
if errorlevel 1 goto :fail

for /f "delims=" %%V in ('git --version') do >>"%LOG%" echo %%V
for /f "delims=" %%V in ('cmake --version ^| findstr /b /c:"cmake version"') do >>"%LOG%" echo %%V

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%VSWHERE%" (
  echo ERROR: No encuentro Visual Studio Installer / Build Tools.
  >>"%LOG%" echo ERROR: vswhere.exe no encontrado.
  >>"%LOG%" echo Instale Visual Studio con Desarrollo para el escritorio con C++.
  goto :fail
)

set "VSINSTALL="
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"

if not defined VSINSTALL (
  echo ERROR: Visual Studio esta instalado, pero falta C++ x64/x86.
  echo Abra Visual Studio Installer y marque Desarrollo para el escritorio con C++.
  >>"%LOG%" echo ERROR: No se encontro Microsoft.VisualStudio.Component.VC.Tools.x86.x64.
  goto :fail
)

echo Compilador C++ encontrado.
>>"%LOG%" echo Visual Studio C++: %VSINSTALL%

rem Fuerza soporte de rutas largas solamente para los procesos Git de este BAT.
rem No cambia la configuracion global del usuario.
set "GIT_CONFIG_COUNT=1"
set "GIT_CONFIG_KEY_0=core.longpaths"
set "GIT_CONFIG_VALUE_0=true"
>>"%LOG%" echo Git core.longpaths temporal: true
>>"%LOG%" echo.

rem Si la 2.17.1 ya compilo NAM correctamente, reutiliza esa DLL y evita repetir todo.
set "DLL="
if exist "%BLD%" (
  for /f "delims=" %%F in ('dir /s /b "%BLD%\NeuralAudioCAPI.dll" 2^>nul') do (
    if not defined DLL set "DLL=%%F"
  )
)
if defined DLL (
  echo Motor NAM ya compilado encontrado. Reutilizando NeuralAudioCAPI.dll...
  >>"%LOG%" echo DLL existente reutilizada: !DLL!
  copy /y "!DLL!" "%~dp0NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
  if errorlevel 1 goto :fail
  if exist "%~dp0PUBLICACION\AmpAccessible.exe" copy /y "!DLL!" "%~dp0PUBLICACION\NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
  if exist "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" copy /y "!DLL!" "%~dp0PUBLICACION_AUTOCONTENIDA\NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
  >>"%LOG%" echo RESULTADO: MOTOR NAM REUTILIZADO CORRECTAMENTE.
  echo MOTOR NAM REUTILIZADO CORRECTAMENTE.
  exit /b 0
)

echo Limpiando carpeta temporal anterior...
if exist "%WORK%" rmdir /s /q "%WORK%" >>"%LOG%" 2>&1
if exist "%WORK%" (
  echo ERROR: No pude limpiar %WORK%.
  >>"%LOG%" echo ERROR: La carpeta temporal quedo bloqueada.
  goto :fail
)
mkdir "%WORK%" >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

echo Descargando NeuralAudio v0.1.3 en ruta corta...
>>"%LOG%" echo.
>>"%LOG%" echo === git clone principal ===
git clone --branch v0.1.3 --depth 1 https://github.com/mikeoliphant/NeuralAudio.git "%SRC%" >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

echo Descargando submodulos...
>>"%LOG%" echo.
>>"%LOG%" echo === git submodule update ===
git -C "%SRC%" submodule sync --recursive >>"%LOG%" 2>&1
git -C "%SRC%" submodule update --init --recursive >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

if not exist "%SRC%\NeuralAudioCAPI\CMakeLists.txt" (
  echo ERROR: La descarga de NeuralAudio esta incompleta. Falta NeuralAudioCAPI.
  >>"%LOG%" echo ERROR: Falta %SRC%\NeuralAudioCAPI\CMakeLists.txt
  goto :fail
)

set "CMAKEGEN="
cmake --help | findstr /C:"Visual Studio 18 2026" >nul 2>nul
if not errorlevel 1 set "CMAKEGEN=Visual Studio 18 2026"
if not defined CMAKEGEN (
  cmake --help | findstr /C:"Visual Studio 17 2022" >nul 2>nul
  if not errorlevel 1 set "CMAKEGEN=Visual Studio 17 2022"
)

if not defined CMAKEGEN (
  echo ERROR: CMake no ofrece un generador compatible con Visual Studio 2022/2026.
  >>"%LOG%" echo ERROR: No se encontro generador Visual Studio 17 2022 ni Visual Studio 18 2026.
  >>"%LOG%" echo Actualice CMake y vuelva a ejecutar este archivo.
  goto :fail
)

echo Generador CMake: %CMAKEGEN%
>>"%LOG%" echo Generador CMake: %CMAKEGEN%

echo Configurando NeuralAudio x64 Release...
>>"%LOG%" echo.
>>"%LOG%" echo === CMake configure ===
cmake -S "%SRC%" -B "%BLD%" -G "%CMAKEGEN%" -A x64 ^
  -DBUILD_NAMCORE=ON ^
  -DNAM_ENABLE_A2_FAST=ON ^
  -DBUILD_INTERNAL_STATIC_WAVENET=ON ^
  -DBUILD_INTERNAL_STATIC_LSTM=ON ^
  -DBUILD_STATIC_INTERNAL_NAMA2=ON ^
  -DBUILD_UTILS=OFF >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

echo Compilando NeuralAudioCAPI.dll...
>>"%LOG%" echo.
>>"%LOG%" echo === CMake build NeuralAudioCAPI ===
cmake --build "%BLD%" --config Release --target NeuralAudioCAPI --parallel 4 >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

set "DLL="
for /f "delims=" %%F in ('dir /s /b "%BLD%\NeuralAudioCAPI.dll" 2^>nul') do (
  if not defined DLL set "DLL=%%F"
)

if not defined DLL (
  echo ERROR: La compilacion termino pero no encontre NeuralAudioCAPI.dll.
  >>"%LOG%" echo ERROR: No se encontro NeuralAudioCAPI.dll dentro de %BLD%.
  goto :fail
)

echo DLL encontrada:
echo %DLL%
>>"%LOG%" echo.
>>"%LOG%" echo DLL encontrada: %DLL%

copy /y "%DLL%" "%~dp0NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
if errorlevel 1 goto :fail

if exist "%~dp0PUBLICACION\AmpAccessible.exe" (
  copy /y "%DLL%" "%~dp0PUBLICACION\NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
)

if exist "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" (
  copy /y "%DLL%" "%~dp0PUBLICACION_AUTOCONTENIDA\NeuralAudioCAPI.dll" >>"%LOG%" 2>&1
)

echo.
echo ============================================================
echo MOTOR NAM PREPARADO CORRECTAMENTE
echo ============================================================
echo.
echo NeuralAudioCAPI.dll ya quedo copiada junto al proyecto.
if exist "%~dp0PUBLICACION\AmpAccessible.exe" (
  echo Tambien quedo copiada dentro de PUBLICACION.
) else (
  echo Ahora se puede ejecutar COMPILAR.bat.
)
echo.
>>"%LOG%" echo.
>>"%LOG%" echo RESULTADO: MOTOR NAM REPARADO CORRECTAMENTE.
if /i not "%~1"=="/AUTO" pause
exit /b 0

:check
where %1 >nul 2>nul
if errorlevel 1 (
  echo ERROR: No encuentro %~2 en el PATH.
  >>"%LOG%" echo ERROR: No se encontro %~1 en PATH.
  exit /b 1
)
for /f "delims=" %%V in ('where %1') do (
  >>"%LOG%" echo %~2: %%V
  goto :check_done
)
:check_done
exit /b 0

:fail
echo.
echo ============================================================
echo NO SE PUDO PREPARAR EL MOTOR NAM
echo ============================================================
echo.
echo Se abrira DIAGNOSTICO_MOTOR_NAM.txt en el Bloc de notas.
echo Envieme ese archivo completo.
echo.
>>"%LOG%" echo.
>>"%LOG%" echo RESULTADO: ERROR.
if /i not "%~1"=="/AUTO" start "" notepad.exe "%LOG%"
if /i not "%~1"=="/AUTO" pause
exit /b 1
