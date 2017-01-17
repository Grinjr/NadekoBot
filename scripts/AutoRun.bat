@ECHO off
@TITLE GrinBot


SET root=%~dp0
CD /D %root%

CLS
ECHO Welcome to GrinBot Auto Restart and Update!
ECHO --------------------------------------------
ECHO 1.Update & Start Bot
ECHO 2.Start Bot
ECHO 3.To exit
ECHO.

CHOICE /C 1234 /M "Enter your choice within 15 seconds, will default to 1:" /T 15 /D 1

:: Note - list ERRORLEVELS in decreasing order
IF ERRORLEVEL 3 GOTO exit
IF ERRORLEVEL 2 GOTO autorun
IF ERRORLEVEL 1 GOTO latestar

:latestar
ECHO Auto Restart and Update with Dev Build (latest)
ECHO Bot will auto update on every restart!
timeout /t 3
CD /D %~dp0GrinBot\src\NadekoBot
dotnet run --configuration Release
ECHO Updating...
timeout /t 3
SET "FILENAME=%~dp0\Latest.bat"
bitsadmin.exe /transfer "Downloading GrinBot (Latest)" /priority high https://github.com/Grinjr/NadekoBot/raw/dev/scripts/Latest.bat "%FILENAME%"
ECHO GrinBot Dev Build (latest) downloaded.
SET root=%~dp0
CD /D %root%
CALL Latest.bat
GOTO latestar

:stablear
ECHO Auto Restart and Update with Stable Build
ECHO Bot will auto update on every restart!
timeout /t 3
CD /D %~dp0NadekoBot\src\NadekoBot
dotnet run --configuration Release
ECHO Updating...
timeout /t 3
SET "FILENAME=%~dp0\Stable.bat"
bitsadmin.exe /transfer "Downloading Nadeko (Stable)" /priority high https://github.com/Kwoth/NadekoBot/raw/master/scripts/Stable.bat "%FILENAME%"
ECHO NadekoBot Stable build downloaded.
SET root=%~dp0
CD /D %root%
CALL Stable.bat
GOTO stablear

:autorun
ECHO Normal Auto Restart
ECHO Bot will not auto update on every restart!
timeout /t 3
CD /D %~dp0GrinBot\src\NadekoBot
dotnet run --configuration Release
goto autorun

:Exit
SET root=%~dp0
CD /D %root%
exit
del NadekoAutoRun.bat
CALL NadekoInstaller.bat
