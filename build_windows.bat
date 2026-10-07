@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem ============================================================
rem PrintVault release build
rem VERSION is the single authoritative release version.
rem ============================================================
set "ROOT=%~dp0"
set "VERSION_FILE=%ROOT%VERSION"
set "SOLUTION=%ROOT%PrintVault.sln"
set "PROJECT=%ROOT%src\PrintVault\PrintVault.csproj"
set "PROPS=%ROOT%Directory.Build.props"
set "COMMANDS=%ROOT%src\PrintVault.Core\Commands.cs"
set "XAML=%ROOT%src\PrintVault\MainWindow.xaml"
set "ISS=%ROOT%installer\PrintVault_3MF.iss"
set "DIST=%ROOT%dist"
set "APPDIST=%DIST%\PrintVault"
set "INSTALLERDIST=%DIST%\installer"
set "LOGDIR=%ROOT%build_logs"
set "LOG=%LOGDIR%\build_output.log"

if not exist "%LOGDIR%" mkdir "%LOGDIR%"
if exist "%LOG%" del /q "%LOG%"

if not exist "%VERSION_FILE%" goto :fail_version
set /p VERSION=<"%VERSION_FILE%"
if "%VERSION%"=="" goto :fail_version

>"%LOG%" echo PrintVault 3MF %VERSION% BUILD
>>"%LOG%" echo Started: %date% %time%
>>"%LOG%" echo Source: %ROOT%
>>"%LOG%" echo Version source: %VERSION_FILE%
>>"%LOG%" echo.

echo ============================================================
echo PrintVault 3MF %VERSION% BUILD
echo ============================================================
echo Version source: %VERSION_FILE%

echo Checking prerequisites...
where dotnet >nul 2>&1
if errorlevel 1 goto :fail_prereq
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
"%DOTNET%" --version >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_prereq

set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" goto :fail_prereq

if not exist "%SOLUTION%" goto :fail_prereq
if not exist "%PROJECT%" goto :fail_prereq
if not exist "%PROPS%" goto :fail_version
if not exist "%COMMANDS%" goto :fail_version
if not exist "%XAML%" goto :fail_version
if not exist "%ISS%" goto :fail_version

rem ------------------------------------------------------------
rem RELEASE PREFLIGHT
rem The PowerShell validator is intentionally separate from this batch
rem file so version checks are readable and produce exact diagnostics.
rem ------------------------------------------------------------
echo Running release version preflight...
echo Preflight root: %ROOT%
echo Preflight script: %ROOT%tools\release_preflight.ps1
set "PREFLIGHT_LOG=%LOGDIR%\version_preflight.log"
if exist "%PREFLIGHT_LOG%" del /q "%PREFLIGHT_LOG%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\release_preflight.ps1" -Root "%ROOT%" > "%PREFLIGHT_LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%PREFLIGHT_LOG%"
type "%PREFLIGHT_LOG%" >> "%LOG%"
if not "%RC%"=="0" goto :fail_version

for /f "delims=" %%V in ('"%DOTNET%" --version') do set "SDKVER=%%V"
echo SDK: %SDKVER%
echo ISCC: %ISCC%
>>"%LOG%" echo SDK: %SDKVER%
>>"%LOG%" echo ISCC: %ISCC%

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%APPDIST%"
mkdir "%INSTALLERDIST%"

rem ============================================================
rem 1/4 RESTORE
rem ============================================================
echo. >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo 1/4 RESTORE >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo.
echo ------------------------------------------------------------
echo 1/4 RESTORE
echo ------------------------------------------------------------
echo Command: "%DOTNET%" restore "%SOLUTION%" >> "%LOG%"
"%DOTNET%" restore "%SOLUTION%" >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo RESTORE EXIT CODE: %RC% >> "%LOG%"
echo RESTORE EXIT CODE: %RC%
if not "%RC%"=="0" goto :fail

rem ============================================================
rem 2/4 BUILD PROJECT
rem ============================================================
echo. >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo 2/4 BUILD PROJECT >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo.
echo ------------------------------------------------------------
echo 2/4 BUILD PROJECT
echo ------------------------------------------------------------
echo Command: "%DOTNET%" build "%PROJECT%" -c Release --no-restore >> "%LOG%"
"%DOTNET%" build "%PROJECT%" -c Release --no-restore >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo BUILD EXIT CODE: %RC% >> "%LOG%"
echo BUILD EXIT CODE: %RC%
if not "%RC%"=="0" goto :fail

rem ============================================================
rem 3/4 PUBLISH
rem ============================================================
echo. >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo 3/4 PUBLISH WIN-X64 >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo.
echo ------------------------------------------------------------
echo 3/4 PUBLISH WIN-X64
echo ------------------------------------------------------------
echo Command: "%DOTNET%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%APPDIST%" >> "%LOG%"
"%DOTNET%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%APPDIST%" >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo PUBLISH EXIT CODE: %RC% >> "%LOG%"
echo PUBLISH EXIT CODE: %RC%
if not "%RC%"=="0" goto :fail

if not exist "%APPDIST%\PrintVault.exe" (
  echo ERROR: PrintVault.exe was not created. >> "%LOG%"
  echo ERROR: PrintVault.exe was not created.
  goto :fail
)

rem ============================================================
rem 4/4 INNO SETUP
rem Inno receives MyAppVersion through /D; the .iss template must not
rem redefine that symbol, otherwise command-line version injection can be shadowed.
rem ============================================================
echo. >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo 4/4 INNO SETUP >> "%LOG%"
echo ------------------------------------------------------------ >> "%LOG%"
echo.
echo ------------------------------------------------------------
echo 4/4 INNO SETUP
echo ------------------------------------------------------------
echo Command: "%ISCC%" /DMyAppVersion=%VERSION% /O"%INSTALLERDIST%" "%ISS%" >> "%LOG%"
"%ISCC%" /DMyAppVersion=%VERSION% /O"%INSTALLERDIST%" "%ISS%" >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
echo INNO SETUP EXIT CODE: %RC% >> "%LOG%"
echo INNO SETUP EXIT CODE: %RC%
if not "%RC%"=="0" goto :fail

set "INSTALLER=%INSTALLERDIST%\PrintVault_3MF_v%VERSION%_Setup.exe"
if not exist "%INSTALLER%" (
  echo ERROR: Expected installer missing: %INSTALLER% >> "%LOG%"
  echo ERROR: Expected installer missing: %INSTALLER%
  goto :fail
)

rem Final artifact sanity: verify the installer filename contains the release version.
for %%F in ("%INSTALLER%") do set "INSTALLER_NAME=%%~nxF"
echo Installer artifact: !INSTALLER_NAME! >> "%LOG%"
echo Installer artifact: !INSTALLER_NAME!
echo !INSTALLER_NAME! | findstr /C:"v%VERSION%_Setup.exe" >nul || goto :fail_version

>>"%LOG%" echo.
>>"%LOG%" echo BUILD SUCCESSFUL
>>"%LOG%" echo VERSION PREFLIGHT: PASS
>>"%LOG%" echo Release: %VERSION%
>>"%LOG%" echo Installer: %INSTALLER%
>>"%LOG%" echo Application: %APPDIST%\PrintVault.exe

echo.
echo ============================================================
echo BUILD SUCCESSFUL
 echo ============================================================
echo Release: %VERSION%
echo Installer: %INSTALLER%
echo Application: %APPDIST%\PrintVault.exe
echo Log: %LOG%
echo.
if /I "%PRINTVAULT_CI%"=="1" exit /b 0
pause
exit /b 0

:fail_version
echo VERSION PREFLIGHT FAILED >> "%LOG%"
echo VERSION PREFLIGHT FAILED
echo Expected release version: %VERSION%
echo See the VERSION PREFLIGHT section above for the exact failed check.
goto :fail

:fail_prereq
echo. >> "%LOG%"
echo PREREQUISITE CHECK FAILED >> "%LOG%"
echo PREREQUISITE CHECK FAILED
goto :fail

:fail
echo. >> "%LOG%"
echo BUILD FAILED - compiler diagnostics follow >> "%LOG%"
echo.
echo BUILD FAILED
echo Full log: %LOG%
echo.
echo ---------------- BUILD LOG ----------------
type "%LOG%"
echo ---------------- END BUILD LOG ----------------
echo The window will remain open.
echo.
if /I "%PRINTVAULT_CI%"=="1" exit /b 1
pause
exit /b 1
