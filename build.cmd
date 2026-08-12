@echo off
REM Builds SINVAUX Aftermath using the C# compiler that ships with Windows.
REM No SDK, no NuGet, no dependencies. Brand assets are embedded, so the output
REM is a single self-contained .exe.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo ERROR: .NET Framework compiler not found at %CSC%
  echo This machine may be missing .NET Framework 4.x.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:"%~dp0Aftermath.exe" ^
  /win32icon:"%~dp0brand\app.ico" ^
  /resource:"%~dp0brand\wordmark-light.png",wordmark-light.png ^
  /resource:"%~dp0brand\wordmark-dark.png",wordmark-dark.png ^
  /resource:"%~dp0brand\monogram-light.png",monogram-light.png ^
  /resource:"%~dp0brand\monogram-dark.png",monogram-dark.png ^
  /resource:"%~dp0brand\app.ico",aftermath.ico ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Management.dll ^
  /reference:System.Core.dll ^
  /reference:System.IO.Compression.dll ^
  /reference:System.IO.Compression.FileSystem.dll ^
  /reference:System.Security.dll ^
  "%~dp0src\Program.cs" "%~dp0src\Theme.cs" "%~dp0src\Brand.cs" "%~dp0src\Widgets.cs" "%~dp0src\Deep.cs" "%~dp0src\Exposure.cs" "%~dp0src\Quarantine.cs" "%~dp0src\Baseline.cs" "%~dp0src\AutoTrigger.cs" "%~dp0src\SweepReport.cs" "%~dp0src\Sweep.cs" "%~dp0src\License.cs" "%~dp0src\Entitlements.cs" "%~dp0src\PdfWriter.cs" "%~dp0src\Exporter.cs" "%~dp0src\UpgradePage.cs" "%~dp0src\DriftScheduler.cs" "%~dp0src\ScanProfile.cs" "%~dp0src\AccountClient.cs" "%~dp0src\AccountGate.cs" "%~dp0src\Json.cs" "%~dp0src\UpdateChecker.cs" "%~dp0src\Ui.cs" "%~dp0src\DetectionWorkspace.cs" "%~dp0src\OwnerWorkspace.cs" "%~dp0src\ExternalAv.cs" "%~dp0src\Remediation.cs" "%~dp0src\LaunchAtLogon.cs" "%~dp0src\NetworkCenter.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo BUILD OK -^> %~dp0Aftermath.exe
