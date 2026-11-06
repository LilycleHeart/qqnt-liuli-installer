@echo off
rem ============================================================================
rem  build.cmd  --  build the QQNT package.json redirect hook DLL
rem
rem  Products (kept in this folder):
rem    dbghelp.probe.dll   probe build: log only, can NEVER rewrite (collect data)
rem    dbghelp.dll         active build: behaviour from C:\liuli-b2\hook.ini or env
rem
rem  Both come from the same hook.c; the only difference is /DLIULI_PROBE=1.
rem  (Same-DLL-plus-config would also work, because the active build defaults to
rem   probe when nothing is configured.  Keeping a physically non-rewriting probe
rem   DLL makes the test machine safer.)
rem
rem  NOTE: hook.c is compiled separately from minhook\src\hook.c because both
rem  are named "hook" and would otherwise overwrite the same hook.obj.
rem ============================================================================
setlocal

set "VCVARS=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (
    echo [FAIL] not found: "%VCVARS%"
    exit /b 1
)

call "%VCVARS%" >nul
if errorlevel 1 (
    echo [FAIL] vcvars64.bat failed
    exit /b 1
)

cd /d "%~dp0"

set "MH_SRC=minhook\src\hook.c minhook\src\trampoline.c minhook\src\buffer.c minhook\src\hde\hde64.c minhook\src\hde\hde32.c"
set "INC=/I minhook\include /I minhook\src /I minhook\src\hde"
rem /MT   static CRT -> no dependency on vcruntime140.dll (important for injection)
rem /utf-8 sources are UTF-8, so Chinese comments are not mis-parsed as GBK
rem /GS-   no security cookie, same style as the existing winmm proxy
set "BASE=/nologo /utf-8 /O2 /MT /GS- /W3 /D_CRT_SECURE_NO_WARNINGS"

if not exist obj\main mkdir obj\main
if not exist obj\mh mkdir obj\mh
del /q obj\main\*.obj obj\mh\*.obj *.exp *.lib 2>nul
type nul > build.log

echo === [1/4] compile MinHook ===
cl %BASE% /c /Foobj\mh\ %INC% %MH_SRC% >> build.log 2>&1
if errorlevel 1 (
    echo [FAIL] MinHook compile failed, see build.log
    type build.log
    exit /b 2
)

echo === [2/4] compile hook.c (PROBE) ===
cl %BASE% /c /DLIULI_PROBE=1 /Foobj\main\ %INC% hook.c >> build.log 2>&1
if errorlevel 1 (
    echo [FAIL] hook.c probe compile failed, see build.log
    type build.log
    exit /b 3
)
echo === [3/4] link dbghelp.probe.dll ===
link /NOLOGO /DLL /NOIMPLIB /NOEXP /OUT:dbghelp.probe.dll /PDBALTPATH:none ^
     obj\main\hook.obj obj\mh\*.obj >> build.log 2>&1
if errorlevel 1 (
    echo [FAIL] probe link failed, see build.log
    type build.log
    exit /b 4
)

echo === [4/4] compile + link dbghelp.dll (ACTIVE) ===
cl %BASE% /c /Foobj\main\ %INC% hook.c >> build.log 2>&1
if errorlevel 1 (
    echo [FAIL] hook.c compile failed, see build.log
    type build.log
    exit /b 5
)
link /NOLOGO /DLL /NOIMPLIB /NOEXP /OUT:dbghelp.dll /PDBALTPATH:none ^
     obj\main\hook.obj obj\mh\*.obj >> build.log 2>&1
if errorlevel 1 (
    echo [FAIL] active link failed, see build.log
    type build.log
    exit /b 6
)

echo.
echo === build OK ===
for %%F in (dbghelp.probe.dll dbghelp.dll) do (
    if exist %%F (echo   %%~fF  %%~zF bytes) else (echo   [MISSING] %%F)
)
echo   log: %~dp0build.log
exit /b 0
