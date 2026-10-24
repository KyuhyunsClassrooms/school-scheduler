@echo off
setlocal
set "VSROOT=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cd /d "%~dp0"
if not exist bin mkdir bin
cl /nologo /utf-8 /O2 /MT /EHsc /W4 /LD /DUNICODE /D_UNICODE /Febin\SchoolDockHook.dll src\DockHook.cpp user32.lib gdi32.lib msimg32.lib comctl32.lib /link /INCREMENTAL:NO || exit /b 1
del /q DockHook.obj bin\SchoolDockHook.exp bin\SchoolDockHook.lib 2>nul
echo native build ok
