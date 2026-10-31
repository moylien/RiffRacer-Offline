@echo off
setlocal
echo Compiling Parse.dll and Parse.NetFx45.dll...
if not exist build mkdir build
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /noconfig /target:library /out:build\Parse.dll /keyfile:local.snk /reference:C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll,C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.dll,C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Core.dll,C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Net.Http.dll Parse.cs
if errorlevel 1 exit /b 1

C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /noconfig /target:library /out:build\Parse.NetFx45.dll /keyfile:local.snk /reference:C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll,C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.dll,C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Core.dll,build\Parse.dll ParseNetFx45.cs
if errorlevel 1 exit /b 1

echo Build succeeded.
endlocal
