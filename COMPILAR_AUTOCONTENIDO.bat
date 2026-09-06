@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "PROJECT=%~dp0GDMAmpAccessible.csproj"

if not exist "%PROJECT%" (
  echo ERROR: No se encontro el archivo de proyecto:
  echo %PROJECT%
  goto :error
)

echo Amp Accessible 2.41.58 - publicacion autocontenida para Windows x64.
echo Esta opcion genera una carpeta mas grande, pero no requiere instalar el runtime en la PC destino.
echo.

set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
set "SDK_LIST_FILE=%TEMP%\GDM_Amp_dotnet_sdks.txt"
if not exist "%DOTNET_EXE%" goto :sdk_error
"%DOTNET_EXE%" --list-sdks >"%SDK_LIST_FILE%" 2>nul
findstr /b /l /c:"8." "%SDK_LIST_FILE%" >nul 2>nul
if errorlevel 1 goto :sdk_error

echo SDK x64: %DOTNET_EXE%
"%DOTNET_EXE%" --version
if errorlevel 1 goto :sdk_error
del /q "%SDK_LIST_FILE%" >nul 2>nul

"%DOTNET_EXE%" restore "%PROJECT%"
if errorlevel 1 goto :error

if exist "%~dp0PUBLICACION_AUTOCONTENIDA" rmdir /s /q "%~dp0PUBLICACION_AUTOCONTENIDA"
"%DOTNET_EXE%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%~dp0PUBLICACION_AUTOCONTENIDA"
if errorlevel 1 goto :error
if not exist "%~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe" goto :error

if exist "%~dp0HISTORIAL_Y_GUIA.txt" copy /y "%~dp0HISTORIAL_Y_GUIA.txt" "%~dp0PUBLICACION_AUTOCONTENIDA\HISTORIAL_Y_GUIA.txt" >nul
if exist "%~dp0NeuralAudioCAPI.dll" copy /y "%~dp0NeuralAudioCAPI.dll" "%~dp0PUBLICACION_AUTOCONTENIDA\NeuralAudioCAPI.dll" >nul

echo.
echo LISTO: %~dp0PUBLICACION_AUTOCONTENIDA\AmpAccessible.exe
if /i not "%~1"=="/AUTO" start "" "%~dp0PUBLICACION_AUTOCONTENIDA"
if /i not "%~1"=="/AUTO" pause
exit /b 0

:sdk_error
del /q "%SDK_LIST_FILE%" >nul 2>nul
echo ERROR: No se encontro .NET 8 SDK x64 en %ProgramFiles%\dotnet.
echo El dotnet de Program Files x86 no se usa para compilar Amp Accessible x64.
if /i not "%~1"=="/AUTO" pause
exit /b 1

:error
echo.
echo LA COMPILACION FALLO. Copie todo el texto de esta ventana.
if /i not "%~1"=="/AUTO" pause
exit /b 1
