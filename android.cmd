@echo off
setlocal

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"

set "MOBILE_PROJECT=%REPO_ROOT%\apps\mobile-app\VinhKhanh.MobileApp.csproj"
set "DOTNET_CLI_HOME=%REPO_ROOT%\.dotnet-home"
set "APPDATA=%DOTNET_CLI_HOME%\AppData\Roaming"
set "NUGET_PACKAGES=%DOTNET_CLI_HOME%\.nuget\packages"
set "APP_SETTINGS_DIR=%REPO_ROOT%\.android-settings"
set "ADB_PATH=C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"

if not exist "%MOBILE_PROJECT%" goto :missing_project
if exist "%ADB_PATH%" goto :adb_ready
echo Khong tim thay adb tai %ADB_PATH%
echo Hay cai Android platform-tools hoac cap nhat script android.cmd.
exit /b 1

:missing_project
echo Khong tim thay mobile project: %MOBILE_PROJECT%
exit /b 1

:adb_ready

if not exist "%DOTNET_CLI_HOME%" mkdir "%DOTNET_CLI_HOME%"
if not exist "%APPDATA%" mkdir "%APPDATA%"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"
if not exist "%APP_SETTINGS_DIR%" mkdir "%APP_SETTINGS_DIR%"

set "DEVICE_ID="
set "DEVICE_COUNT=0"
set "ADB_DEVICES_FILE=%TEMP%\vk-food-adb-devices.txt"

"%ADB_PATH%" devices > "%ADB_DEVICES_FILE%"
for /f "skip=1 tokens=1,2" %%A in (%ADB_DEVICES_FILE%) do (
    if "%%B"=="device" (
        set /a DEVICE_COUNT+=1
        if not defined DEVICE_ID set "DEVICE_ID=%%A"
    )
)
del "%ADB_DEVICES_FILE%" >nul 2>nul

if not defined DEVICE_ID (
    echo Khong co thiet bi Android nao dang online trong adb.
    echo Neu ban dang dung Wi-Fi debug, hay pair/connect lai truoc.
    exit /b 1
)

if not "%DEVICE_COUNT%"=="1" (
    echo Dang co %DEVICE_COUNT% thiet bi online. Script se dung thiet bi dau tien: %DEVICE_ID%
)

set "ANDROID_SERIAL=%DEVICE_ID%"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"

echo Dang chay app Android tren thiet bi: %DEVICE_ID%
dotnet build "%MOBILE_PROJECT%" -t:Run -f net10.0-android --no-restore -p:AppSettingsDirectory="%APP_SETTINGS_DIR%"
exit /b %errorlevel%
