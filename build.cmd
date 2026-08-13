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
  "%~dp0src\Program.cs" "%~dp0src\Theme.cs" "%~dp0src\Brand.cs" "%~dp0src\Widgets.cs" "%~dp0src\Deep.cs" "%~dp0src\Exposure.cs" "%~dp0src\Quarantine.cs" "%~dp0src\Baseline.cs" "%~dp0src\AutoTrigger.cs" "%~dp0src\SweepReport.cs" "%~dp0src\Sweep.cs" "%~dp0src\License.cs" "%~dp0src\Entitlements.cs" "%~dp0src\PdfWriter.cs" "%~dp0src\Exporter.cs" "%~dp0src\UpgradePage.cs" "%~dp0src\DriftScheduler.cs" "%~dp0src\ScanProfile.cs" "%~dp0src\AccountClient.cs" "%~dp0src\AccountGate.cs" "%~dp0src\Json.cs" "%~dp0src\UpdateChecker.cs" "%~dp0src\Ui.cs" "%~dp0src\DetectionWorkspace.cs" "%~dp0src\OwnerWorkspace.cs" "%~dp0src\ExternalAv.cs" "%~dp0src\Remediation.cs" "%~dp0src\LaunchAtLogon.cs" "%~dp0src\NetworkCenter.cs" "%~dp0src\SimulatedOrgData.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo BUILD OK -^> %~dp0Aftermath.exe

REM ---------------------------------------------------------------------
REM Optional code signing. Skipped entirely (build still succeeds) unless
REM both SINVAUX_PFX and SINVAUX_PFX_PASSWORD are set - most dev machines
REM won't have a certificate, and this must never block a normal build.
REM
REM   set SINVAUX_PFX=C:\path\to\sinvaux-codesign.pfx
REM   set SINVAUX_PFX_PASSWORD=your-pfx-password
REM   build.cmd
REM
REM The certificate itself is bought from a CA (DigiCert/Sectigo/SSL.com
REM etc.) and needs business identity verification - that step happens
REM outside this script, on the CA's own site. Once you have the .pfx,
REM this just runs signtool against it. Timestamping (SHA256 via
REM DigiCert's public timestamp server, usable regardless of which CA
REM issued the cert) means the signature stays valid after the cert
REM itself expires - without it, every signed build silently stops
REM verifying the day the certificate lapses.
REM ---------------------------------------------------------------------

if "%SINVAUX_PFX%"=="" (
  echo Not signed - set SINVAUX_PFX and SINVAUX_PFX_PASSWORD to sign this build.
  exit /b 0
)
if "%SINVAUX_PFX_PASSWORD%"=="" (
  echo Not signed - SINVAUX_PFX is set but SINVAUX_PFX_PASSWORD is not.
  exit /b 0
)
if not exist "%SINVAUX_PFX%" (
  echo WARNING: SINVAUX_PFX points to a file that does not exist - %SINVAUX_PFX%
  echo Build succeeded but was NOT signed.
  exit /b 0
)

REM signtool's install path is versioned (10.0.xxxxx.0) and varies by
REM machine/SDK update, so it's located dynamically rather than hardcoded -
REM same reasoning as CSC above being the one hardcoded compiler path, but
REM signtool doesn't have a single stable path the way the .NET Framework
REM compiler does. SINVAUX_SIGNTOOL can override this search entirely if
REM you already know the path.
set SIGNTOOL=%SINVAUX_SIGNTOOL%
if "%SIGNTOOL%"=="" (
  for /f "delims=" %%S in ('dir /s /b "%ProgramFiles(x86)%\Windows Kits\10\bin\x64\signtool.exe" 2^>nul') do set SIGNTOOL=%%S
)
if "%SIGNTOOL%"=="" (
  for /f "delims=" %%S in ('dir /s /b "%ProgramFiles(x86)%\Windows Kits\10\bin\signtool.exe" 2^>nul') do set SIGNTOOL=%%S
)
if "%SIGNTOOL%"=="" (
  echo WARNING: signtool.exe not found ^(no Windows SDK?^). Set SINVAUX_SIGNTOOL to its exact path.
  echo Build succeeded but was NOT signed.
  exit /b 0
)

echo Signing with %SIGNTOOL% ...
"%SIGNTOOL%" sign /f "%SINVAUX_PFX%" /p "%SINVAUX_PFX_PASSWORD%" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "%~dp0Aftermath.exe"
if errorlevel 1 (
  echo WARNING: signing failed - see signtool's output above. Build succeeded but was NOT signed.
  exit /b 0
)

"%SIGNTOOL%" verify /pa "%~dp0Aftermath.exe"
if errorlevel 1 (
  echo WARNING: signtool reported the signature as invalid after signing - investigate before shipping this build.
  exit /b 1
)

echo SIGNED -^> %~dp0Aftermath.exe
