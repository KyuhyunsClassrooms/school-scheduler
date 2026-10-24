@echo off
rem School widget build (.NET Framework 4.x csc)
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d %~dp0
if not exist bin mkdir bin
%CSC% /nologo /target:winexe /platform:x64 /codepage:65001 /win32manifest:src\app.manifest /out:bin\SchoolWidget.exe ^
  /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Security.dll ^
  /resource:src\school-events.json,school-events.json ^
  src\Program.cs src\CalendarData.cs src\CalendarRenderer.cs src\NativeDialogs.cs src\NativeWidget.cs src\ScheduleFeed.cs src\FeedConnectionDialog.cs || exit /b 1
call build-native.cmd || exit /b 1
echo build ok
