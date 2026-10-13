@echo off
rem ===========================================================================
rem  Compila NtfsPermissionsReporter.exe con el compilador que ya trae Windows
rem  (.NET Framework 4.x). No hace falta instalar Visual Studio ni nada mas.
rem  Uso:  build.bat          (doble clic)
rem        build.bat /ci      (sin pausa, para GitHub Actions)
rem ===========================================================================
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo No se encuentra csc.exe. Instala o activa .NET Framework 4.8.
  if /i not "%1"=="/ci" pause
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ^
  /out:dist\NtfsPermissionsReporter.exe ^
  /win32icon:assets\app.ico /win32manifest:assets\app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.DirectoryServices.dll /r:System.IO.Compression.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo ERROR al compilar.
  if /i not "%1"=="/ci" pause
  exit /b 1
)
echo.
echo OK: dist\NtfsPermissionsReporter.exe
if /i not "%1"=="/ci" pause
