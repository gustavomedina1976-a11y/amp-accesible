@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "PROJECT=%~dp0GDMAmpAccessible.csproj"

if not exist "%PROJECT%" (
  echo ERROR: No se encontro el archivo de proyecto:
  echo %PROJECT%
  goto :error
)

echo Amp Accessible 2.41.58 - compilacion para Windows x64
echo.

rem IMPORTANTE: usar exclusivamente el SDK x64 de Program Files.
rem No se usa el host dotnet de Program Files (x86).
set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
set "SDK_LIST_FILE=%TEMP%\GDM_Amp_dotnet_sdks.txt"

if not exist "%DOTNET_EXE%" (
  echo ERROR: No se encontro .NET 8 SDK x64 en:
  echo %DOTNET_EXE%
  echo.
  echo Un dotnet.exe de Program Files x86 o un Runtime sin SDK no alcanza para compilar.
  goto :error
)

"%DOTNET_EXE%" --list-sdks >"%SDK_LIST_FILE%" 2>nul
findstr /b /c:"8." "%SDK_LIST_FILE%" >nul 2>nul
if errorlevel 1 (
  echo ERROR: Se encontro dotnet x64, pero no existe un SDK 8.x instalado.
  echo Ruta comprobada: %DOTNET_EXE%
  del /q "%SDK_LIST_FILE%" >nul 2>nul
  goto :error
)
del /q "%SDK_LIST_FILE%" >nul 2>nul

echo .NET 8 SDK x64 encontrado:
echo %DOTNET_EXE%
echo Version SDK:
"%DOTNET_EXE%" --version
if errorlevel 1 goto :error
echo.

echo Restaurando NAudio 2.3.0...
"%DOTNET_EXE%" restore "%PROJECT%"
if errorlevel 1 goto :error

echo.
echo Eliminando publicacion anterior...
if exist "%~dp0PUBLICACION" rmdir /s /q "%~dp0PUBLICACION"
if exist "%~dp0PUBLICACION" (
  echo ERROR: No se pudo eliminar PUBLICACION. Cierre Amp Accessible si esta abierto.
  goto :error
)

echo Publicando aplicacion...
"%DOTNET_EXE%" publish "%PROJECT%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o "%~dp0PUBLICACION"
if errorlevel 1 goto :error

if not exist "%~dp0PUBLICACION\AmpAccessible.exe" (
  echo ERROR: dotnet publish finalizo pero no genero PUBLICACION\AmpAccessible.exe.
  goto :error
)

if exist "%~dp0HISTORIAL_Y_GUIA.txt" copy /y "%~dp0HISTORIAL_Y_GUIA.txt" "%~dp0PUBLICACION\HISTORIAL_Y_GUIA.txt" >nul

if not exist "%~dp0NeuralAudioCAPI.dll" (
  for /f "delims=" %%F in ('dir /s /b "%~dp0MOTOR_NAM\build\NeuralAudioCAPI.dll" 2^>nul') do (
    if not exist "%~dp0NeuralAudioCAPI.dll" copy /y "%%F" "%~dp0NeuralAudioCAPI.dll" >nul
  )
)

if exist "%~dp0NeuralAudioCAPI.dll" (
  copy /y "%~dp0NeuralAudioCAPI.dll" "%~dp0PUBLICACION\NeuralAudioCAPI.dll" >nul
  echo Motor NAM copiado a PUBLICACION.
) else (
  echo AVISO: NeuralAudioCAPI.dll no esta presente. Amp Accessible funcionara con los amplificadores internos.
  echo Para usar NAM ejecute REPARAR_MOTOR_NAM.bat y luego COMPILAR.bat otra vez.
)

echo.
echo COMPILACION COMPLETADA CORRECTAMENTE.
echo Ejecutable: %~dp0PUBLICACION\AmpAccessible.exe
echo.
if /i not "%~1"=="/AUTO" start "" "%~dp0PUBLICACION"
if /i not "%~1"=="/AUTO" pause
exit /b 0

:error
echo.
echo LA COMPILACION FALLO. Copie todo el texto de esta ventana para revisar el error.
if /i not "%~1"=="/AUTO" pause
exit /b 1
