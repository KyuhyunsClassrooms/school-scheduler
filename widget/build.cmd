@echo off
rem School widget build (.NET Framework 4.x csc)
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d %~dp0
if not exist bin mkdir bin
rem Optional: src\google-client.json (your own OAuth desktop client, not in git) enables one-click Google Tasks login.
rem SCHOOL_PUBLIC_BUILD=1 leaves it out; build-installer.ps1 sets that unless -Personal is given.
set GOOGLECLIENT=
if not defined SCHOOL_PUBLIC_BUILD if exist src\google-client.json set GOOGLECLIENT=/resource:src\google-client.json,google-client.json
%CSC% /nologo /target:winexe /platform:x64 /codepage:65001 /win32manifest:src\app.manifest /out:bin\SchoolWidget.exe ^
  /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Security.dll ^
  /resource:src\school-events.json,school-events.json %GOOGLECLIENT% ^
  src\Program.cs src\CalendarData.cs src\CalendarRenderer.cs src\NativeDialogs.cs src\NativeWidget.cs src\ScheduleFeed.cs src\FeedConnectionDialog.cs src\TimetableCache.cs src\GoogleTasks.cs || exit /b 1
call "%~dp0build-native.cmd" || exit /b 1
echo build ok
