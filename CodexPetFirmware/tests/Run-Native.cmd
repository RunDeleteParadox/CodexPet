@echo off
setlocal
cd /d "%~dp0.."
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "CODEXPET_VS=%%i"
if not defined CODEXPET_VS exit /b 1
call "%CODEXPET_VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
if not exist artifacts\native mkdir artifacts\native
cl /nologo /std:c++17 /EHsc /O2 /W1 /Itests\native /Isrc src\avatar_engine.cpp src\codexpet_protocol_checks.cpp tests\native\engine_tests.cpp /Fe:artifacts\native\engine_tests.exe /Fo:artifacts\native\
if errorlevel 1 exit /b 1
artifacts\native\engine_tests.exe artifacts\native %*
