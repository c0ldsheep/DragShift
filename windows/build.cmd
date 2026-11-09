@echo off
rem Builds DragShift.exe on Windows (needs the .NET SDK: winget install Microsoft.DotNet.SDK.10)
cd /d "%~dp0"
dotnet run --project tests\CoreTests -c Release || exit /b 1
dotnet build src\DragShift -c Release -o ..\build\windows --nologo -v quiet || exit /b 1
del /q ..\build\windows\DragShift.exe.config 2>nul
echo Built ..\build\windows\DragShift.exe
echo Check it on this PC:  ..\build\windows\DragShift.exe --selftest %TEMP%\dragshift-selftest
