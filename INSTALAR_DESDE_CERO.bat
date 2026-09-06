@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem No se puede ejecutar correctamente un BAT aislado desde dentro de un ZIP.
rem Windows puede copiar solo este archivo a TEMP y dejar fuera el proyecto y los BAT auxiliares.
if not exist "%~dp0REPARAR_MOTOR_NAM.bat" goto :paquete_no_extraido
if not exist "%~dp0COMPILAR.bat" goto :paquete_no_extraido
if not exist "%~dp0GDMAmpAccessible.csproj" goto :paquete_no_extraido

title GDM Amp Accessible 2.41.58 - Instalacion desde cero

set "LOG=%~dp0DIAGNOSTICO_INSTALACION.txt"
>"%LOG%" echo ============================================================
>>"%LOG%" echo GDM Amp Accessible 2.41.58 - Instalacion desde cero
>>"%LOG%" echo Fecha: %date% %time%
>>"%LOG%" echo Carpeta fuente: %CD%
>>"%LOG%" echo ============================================================
>>"%LOG%" echo.

echo ============================================================
echo GDM AMP ACCESSIBLE 2.41.58 - INSTALACION DESDE CERO
echo ============================================================
echo.
echo Este instalador NO cierra esta ventana para elevarse como administrador.
echo Amp Accessible se instalara para el usuario actual, sin requerir UAC.
echo Si falta una dependencia, su instalador puede pedir permisos por separado.
echo.
echo Se comprobaran: .NET 8 SDK x64, Git, CMake y Visual Studio C++.
echo Luego se prepara NAM, se compila Amp Accessible y se crea un acceso directo.
echo.

call :refresh_path

set "WINGET_AVAILABLE=1"
where winget >nul 2>nul
if errorlevel 1 (
  set "WINGET_AVAILABLE=0"
  echo AVISO: Windows Package Manager, winget, no esta disponible.
  echo Si ya estan instaladas todas las dependencias, el proceso puede continuar.
  >>"%LOG%" echo AVISO: winget no disponible.
)

call :ensure_dotnet_sdk
if errorlevel 1 goto :fail
call :ensure_tool git Git.Git "Git para Windows"
if errorlevel 1 goto :fail
call :ensure_tool cmake Kitware.CMake "CMake"
if errorlevel 1 goto :fail
call :ensure_visual_cpp
if errorlevel 1 goto :fail

call :refresh_path

echo.
echo Todas las herramientas de compilacion estan disponibles.
echo Preparando el motor NAM...
>>"%LOG%" echo.
>>"%LOG%" echo === REPARAR_MOTOR_NAM ===
pushd "%~dp0"
call "%~dp0REPARAR_MOTOR_NAM.bat" /AUTO >>"%LOG%" 2>&1
set "NAM_RC=%ERRORLEVEL%"
popd
if not "%NAM_RC%"=="0" goto :fail
if errorlevel 1 (
  echo ERROR: fallo la preparacion del motor NAM.
  goto :fail
)

echo Compilando Amp Accessible...
>>"%LOG%" echo.
>>"%LOG%" echo === COMPILAR ===
pushd "%~dp0"
call "%~dp0COMPILAR.bat" /AUTO >>"%LOG%" 2>&1
set "BUILD_RC=%ERRORLEVEL%"
popd
if not "%BUILD_RC%"=="0" goto :fail
if errorlevel 1 (
  echo ERROR: fallo la compilacion de Amp Accessible.
  goto :fail
)

if not exist "%~dp0PUBLICACION\AmpAccessible.exe" (
  echo ERROR: la compilacion termino pero no existe PUBLICACION\AmpAccessible.exe.
  >>"%LOG%" echo ERROR: PUBLICACION\AmpAccessible.exe no encontrado.
  goto :fail
)

rem Instalacion por usuario: no requiere permisos de administrador.
set "INSTALLDIR=%LOCALAPPDATA%\Programs\GDM Amp Accessible"
echo Instalando para el usuario actual en:
echo %INSTALLDIR%
>>"%LOG%" echo Instalacion final: %INSTALLDIR%

if exist "%INSTALLDIR%" rmdir /s /q "%INSTALLDIR%" >>"%LOG%" 2>&1
if exist "%INSTALLDIR%" (
  echo ERROR: no pude reemplazar la instalacion anterior.
  echo Cierre Amp Accessible si esta abierto y vuelva a ejecutar este instalador.
  >>"%LOG%" echo ERROR: INSTALLDIR no se pudo limpiar.
  goto :fail
)
mkdir "%INSTALLDIR%" >>"%LOG%" 2>&1
if errorlevel 1 goto :fail
xcopy "%~dp0PUBLICACION\*" "%INSTALLDIR%\" /E /I /Y /Q >>"%LOG%" 2>&1
if errorlevel 1 goto :fail
if exist "%~dp0HISTORIAL_Y_GUIA.txt" copy /y "%~dp0HISTORIAL_Y_GUIA.txt" "%INSTALLDIR%\HISTORIAL_Y_GUIA.txt" >>"%LOG%" 2>&1

set "TARGET=%INSTALLDIR%\AmpAccessible.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$target=$env:TARGET; $ws=New-Object -ComObject WScript.Shell;" ^
  "$desktop=[Environment]::GetFolderPath('Desktop'); $lnk=$ws.CreateShortcut((Join-Path $desktop 'Amp Accessible.lnk')); $lnk.TargetPath=$target; $lnk.WorkingDirectory=(Split-Path $target); $lnk.Description='GDM Amp Accessible'; $lnk.Save();" ^
  "$programs=[Environment]::GetFolderPath('Programs'); if($programs){$lnk2=$ws.CreateShortcut((Join-Path $programs 'Amp Accessible.lnk')); $lnk2.TargetPath=$target; $lnk2.WorkingDirectory=(Split-Path $target); $lnk2.Description='GDM Amp Accessible'; $lnk2.Save()}" >>"%LOG%" 2>&1

if errorlevel 1 (
  echo AVISO: el programa se instalo, pero no pude crear uno de los accesos directos.
  >>"%LOG%" echo AVISO: fallo al crear acceso directo.
)

echo Integrando archivos .nam con el Explorador de Windows...
>>"%LOG%" echo Registrando asociacion .nam y menu contextual por usuario.
reg.exe add "HKCU\Software\Classes\.nam" /ve /d "GDMAmpAccessible.NAM" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\.nam\OpenWithProgids" /v "GDMAmpAccessible.NAM" /t REG_NONE /d "" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\GDMAmpAccessible.NAM" /ve /d "Modelo Neural Amp Modeler" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\GDMAmpAccessible.NAM\DefaultIcon" /ve /d "\"%TARGET%\",0" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\GDMAmpAccessible.NAM\shell\open" /ve /d "Abrir con Amp Accessible" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\GDMAmpAccessible.NAM\shell\open\command" /ve /d "\"%TARGET%\" \"%%1\"" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\Applications\AmpAccessible.exe\SupportedTypes" /v ".nam" /t REG_SZ /d "" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\Applications\AmpAccessible.exe\shell\open\command" /ve /d "\"%TARGET%\" \"%%1\"" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Load" /ve /d "Cargar y guardar en Amp Accessible" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Load\command" /ve /d "\"%TARGET%\" \"%%1\"" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Import" /ve /d "Agregar al Banco NAM de Amp Accessible" /f >>"%LOG%" 2>&1
reg.exe add "HKCU\Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Import\command" /ve /d "\"%TARGET%\" --import-nam \"%%1\"" /f >>"%LOG%" 2>&1

echo.
echo ============================================================
echo INSTALACION COMPLETADA CORRECTAMENTE
echo ============================================================
echo.
echo Programa instalado en:
echo %TARGET%
echo.
echo Se creo un acceso directo llamado Amp Accessible en el Escritorio.
echo Los archivos .nam quedaron integrados con Amp Accessible y su Banco NAM.
echo Instale tambien el driver ASIO oficial de la interfaz de audio que vaya a utilizar.
echo.
>>"%LOG%" echo RESULTADO: INSTALACION COMPLETADA CORRECTAMENTE.
start "" "%INSTALLDIR%"
pause
exit /b 0

:paquete_no_extraido
cls
echo ============================================================
echo AMP ACCESSIBLE - EL ZIP NO ESTA EXTRAIDO COMPLETO
echo ============================================================
echo.
echo Este archivo se ejecuto sin encontrar los archivos que deben estar a su lado.
echo Esto ocurre normalmente al abrir INSTALAR_DESDE_CERO.bat directamente dentro del ZIP.
echo.
echo SOLUCION:
echo 1. Cierre esta ventana.
echo 2. En el Explorador de archivos seleccione el ZIP de Amp Accessible.
echo 3. Use Extraer todo y elija una carpeta normal.
echo 4. Entre a la carpeta Amp_Accessible_v2.41.58 ya extraida.
echo 5. Ejecute INSTALAR_DESDE_CERO.bat desde esa carpeta.
echo.
echo No se perdio ninguna instalacion ni ningun banco.
echo.
pause
exit /b 2

:ensure_dotnet_sdk
set "DOTNET_X64=%ProgramFiles%\dotnet\dotnet.exe"
set "SDK_LIST_FILE=%TEMP%\GDM_Amp_dotnet_sdks.txt"
call :check_dotnet8_x64
if not errorlevel 1 (
  echo .NET 8 SDK x64: encontrado.
  echo Ruta: %DOTNET_X64%
  "%DOTNET_X64%" --version
  >>"%LOG%" echo .NET 8 SDK x64: %DOTNET_X64%
  >>"%LOG%" "%DOTNET_X64%" --list-sdks
  exit /b 0
)

if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
  >>"%LOG%" echo AVISO: existe dotnet x86, pero no se usara para compilar win-x64.
)

if "%WINGET_AVAILABLE%"=="0" (
  echo ERROR: no se encontro .NET 8 SDK x64.
  echo Instale .NET 8 SDK x64 en Program Files\dotnet y vuelva a ejecutar este instalador.
  >>"%LOG%" echo ERROR: .NET 8 SDK x64 ausente y winget no disponible.
  exit /b 1
)

echo .NET 8 SDK x64 no esta disponible. Intentando instalarlo...
echo Si Windows solicita permisos para ESTE instalador, aceptelos. Esta ventana permanecera abierta.
>>"%LOG%" echo Instalando Microsoft.DotNet.SDK.8 x64 con winget.
winget install --id Microsoft.DotNet.SDK.8 -e --source winget --architecture x64 --accept-package-agreements --accept-source-agreements >>"%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: no se pudo instalar .NET 8 SDK x64 automaticamente.
  >>"%LOG%" echo ERROR: winget fallo para Microsoft.DotNet.SDK.8 x64.
  exit /b 1
)

call :refresh_path
call :check_dotnet8_x64
if errorlevel 1 (
  echo ERROR: .NET se instalo, pero el SDK 8 x64 aun no aparece en Program Files\dotnet.
  echo Reinicie Windows y ejecute nuevamente este mismo instalador.
  >>"%LOG%" echo ERROR: SDK 8 x64 no visible despues de instalar.
  exit /b 1
)

echo .NET 8 SDK x64 instalado correctamente.
exit /b 0

:check_dotnet8_x64
if not exist "%DOTNET_X64%" exit /b 1
"%DOTNET_X64%" --list-sdks >"%SDK_LIST_FILE%" 2>nul
findstr /b /c:"8." "%SDK_LIST_FILE%" >nul 2>nul
set "SDK_CHECK_ERROR=%ERRORLEVEL%"
del /q "%SDK_LIST_FILE%" >nul 2>nul
if not "%SDK_CHECK_ERROR%"=="0" exit /b 1
exit /b 0

:ensure_tool
set "TOOL=%~1"
set "PACKAGE=%~2"
set "FRIENDLY=%~3"
where %TOOL% >nul 2>nul
if not errorlevel 1 (
  echo %FRIENDLY%: encontrado.
  for /f "delims=" %%P in ('where %TOOL%') do >>"%LOG%" echo %FRIENDLY%: %%P
  exit /b 0
)

if "%WINGET_AVAILABLE%"=="0" (
  echo ERROR: %FRIENDLY% no esta instalado y winget no esta disponible para instalarlo.
  echo Consulte HISTORIAL_Y_GUIA.txt para instalar esta dependencia manualmente.
  >>"%LOG%" echo ERROR: falta %FRIENDLY% y winget no esta disponible.
  exit /b 1
)

echo %FRIENDLY% no esta instalado. Intentando instalarlo...
echo Si Windows solicita permisos para ESTE instalador, aceptelos. Esta ventana permanecera abierta.
>>"%LOG%" echo Instalando %FRIENDLY% con winget, paquete %PACKAGE%.
winget install --id "%PACKAGE%" -e --source winget --accept-package-agreements --accept-source-agreements >>"%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: no se pudo instalar %FRIENDLY% automaticamente.
  >>"%LOG%" echo ERROR: winget fallo para %PACKAGE%.
  exit /b 1
)
call :refresh_path
where %TOOL% >nul 2>nul
if errorlevel 1 (
  echo ERROR: %FRIENDLY% se instalo pero todavia no aparece en PATH.
  echo Reinicie Windows y vuelva a ejecutar este mismo instalador.
  >>"%LOG%" echo ERROR: %TOOL% no visible en PATH despues de instalar.
  exit /b 1
)
echo %FRIENDLY% instalado correctamente.
exit /b 0

:ensure_visual_cpp
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
  set "VSINSTALL="
  for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
  if defined VSINSTALL (
    echo Visual Studio C++ x64/x86: encontrado.
    >>"%LOG%" echo Visual Studio C++: !VSINSTALL!
    exit /b 0
  )
)

if exist "%VSWHERE%" (
  set "VSANY="
  for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -property installationPath`) do set "VSANY=%%I"
  if defined VSANY (
    set "VSSETUP=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\setup.exe"
    if not exist "!VSSETUP!" set "VSSETUP=%ProgramFiles%\Microsoft Visual Studio\Installer\setup.exe"
    if exist "!VSSETUP!" (
      echo Visual Studio esta instalado pero falta C++. Agregando la carga de trabajo C++...
      echo El instalador de Visual Studio puede solicitar permisos; Amp Accessible no cerrara esta ventana.
      >>"%LOG%" echo Modificando Visual Studio existente: !VSANY!
      "!VSSETUP!" modify --installPath "!VSANY!" --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --norestart --wait >>"%LOG%" 2>&1
      set "VSINSTALL="
      for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
      if defined VSINSTALL (
        echo Visual Studio C++ agregado correctamente.
        >>"%LOG%" echo Visual Studio C++: !VSINSTALL!
        exit /b 0
      )
    )
  )
)

if "%WINGET_AVAILABLE%"=="0" (
  echo ERROR: falta Visual Studio Build Tools con C++ y winget no esta disponible.
  echo Instale Desarrollo para el escritorio con C++ y vuelva a ejecutar este instalador.
  >>"%LOG%" echo ERROR: falta C++ y winget no disponible.
  exit /b 1
)

echo Visual Studio Build Tools con C++ no esta completo. Intentando instalarlo...
echo El instalador de Visual Studio puede solicitar permisos; Amp Accessible no cerrara esta ventana.
set "VSPACKAGE=Microsoft.VisualStudio.2022.BuildTools"
winget show --id Microsoft.VisualStudio.2026.BuildTools -e --source winget >nul 2>nul
if not errorlevel 1 set "VSPACKAGE=Microsoft.VisualStudio.2026.BuildTools"
>>"%LOG%" echo Paquete Build Tools seleccionado: !VSPACKAGE!
winget install --id "!VSPACKAGE!" -e --source winget --accept-package-agreements --accept-source-agreements --override "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended" >>"%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: no se pudo instalar Visual Studio Build Tools con C++.
  >>"%LOG%" echo ERROR: fallo winget/Visual Studio Installer para !VSPACKAGE!.
  exit /b 1
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo ERROR: Visual Studio Installer no quedo disponible.
  >>"%LOG%" echo ERROR: vswhere.exe no encontrado luego de instalar Build Tools.
  exit /b 1
)
set "VSINSTALL="
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if not defined VSINSTALL (
  echo ERROR: Build Tools se instalo, pero falta el componente C++ x64/x86.
  echo Abra Visual Studio Installer y marque Desarrollo para el escritorio con C++.
  >>"%LOG%" echo ERROR: Microsoft.VisualStudio.Component.VC.Tools.x86.x64 ausente.
  exit /b 1
)
echo Visual Studio C++ instalado correctamente.
>>"%LOG%" echo Visual Studio C++: !VSINSTALL!
exit /b 0

:refresh_path
set "PATH=%ProgramFiles%\dotnet;%ProgramFiles%\Git\cmd;%ProgramFiles%\CMake\bin;%LocalAppData%\Microsoft\WinGet\Links;%PATH%"
exit /b 0

:fail
echo.
echo ============================================================
echo LA INSTALACION NO PUDO COMPLETARSE
echo ============================================================
echo.
echo Esta ventana permanecera abierta.
echo Se abrira DIAGNOSTICO_INSTALACION.txt.
echo Envieme ese archivo para revisar el error.
echo.
>>"%LOG%" echo RESULTADO: ERROR.
start "" notepad.exe "%LOG%"
pause
exit /b 1
