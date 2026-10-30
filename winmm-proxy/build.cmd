@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cd /d "%~dp0"
cl /nologo /O1 /GS- /LD winmm.c /link /NOLOGO /NODEFAULTLIB /ENTRY:DllMain kernel32.lib /OUT:winmm.dll
