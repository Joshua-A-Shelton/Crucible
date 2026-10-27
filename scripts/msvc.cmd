@echo off
rem Runs a command inside Visual Studio's x64 developer environment (vcvars64.bat),
rem for building from a plain terminal or over SSH (CLAUDE.md section 6):
rem     scripts\msvc.cmd cmake --preset debug
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo msvc.cmd: vswhere.exe not found; is Visual Studio installed? 1>&2
    exit /b 1
)
set "VSINSTALL="
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL (
    echo msvc.cmd: no Visual Studio with the x64 C++ tools found 1>&2
    exit /b 1
)
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
%*
