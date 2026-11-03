@echo off
cd /d "%~dp0"
if "%~1"=="" (
  echo Drag PNG, JPG or GIF files onto this script.
  pause
  exit /b 1
)
if exist EmoteImporter.exe (
  EmoteImporter.exe %*
) else (
  py -3 import_emotes.py %*
)
pause
