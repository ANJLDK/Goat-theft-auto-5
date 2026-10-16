@echo off
setlocal EnableExtensions
title Goat Theft Auto 5 - extract goat models

rem ==================================================================================
rem  Extracts the goat models, textures and animations from YOUR OWN copy of Goat Simulator
rem  into ..\exports using UModel (UE Viewer). Nothing is downloaded from Goat Simulator;
rem  the files come from your install. They belong to Coffee Stain Studios: use them for your
rem  own offline game and do not upload them anywhere.
rem
rem  Usage:  get_models.bat                      (asks for the game folder)
rem          get_models.bat "D:\Games\GoatSimulator"
rem ==================================================================================

set "HERE=%~dp0"
set "OUT=%HERE%..\exports"
set "GAME=%~1"

set "GUESS="
if exist "%ProgramFiles(x86)%\Steam\steamapps\common\GoatSimulator\GoatGame\Content\Packages\Goat.upk" set "GUESS=%ProgramFiles(x86)%\Steam\steamapps\common\GoatSimulator"
if exist "%ProgramFiles%\Steam\steamapps\common\GoatSimulator\GoatGame\Content\Packages\Goat.upk" set "GUESS=%ProgramFiles%\Steam\steamapps\common\GoatSimulator"
if defined GAME goto :haveGame
echo.
echo  Where is Goat Simulator installed? This is the folder that contains "GoatGame".
echo  In Steam: right click Goat Simulator - Manage - Browse local files.
if defined GUESS echo  Found one here, press Enter to use it: %GUESS%
echo.
set /p "GAME= Game folder: "
if not defined GAME set "GAME=%GUESS%"
:haveGame

rem strip quotes and a trailing backslash
set "GAME=%GAME:"=%"
if "%GAME:~-1%"=="\" set "GAME=%GAME:~0,-1%"

if not exist "%GAME%\GoatGame\Content\Packages\Goat.upk" (
  echo.
  echo  ERROR: "%GAME%\GoatGame\Content\Packages\Goat.upk" was not found.
  echo  Give the folder that contains "GoatGame", for example
  echo  C:\Program Files ^(x86^)\Steam\steamapps\common\GoatSimulator
  goto :fail
)
set "CONTENT=%GAME%\GoatGame\Content"

rem ---- find or fetch UModel ---------------------------------------------------------
set "UMODEL=%HERE%umodel.exe"
if not exist "%UMODEL%" if exist "%HERE%umodel\umodel.exe" set "UMODEL=%HERE%umodel\umodel.exe"
if not exist "%UMODEL%" (
  echo.
  echo  umodel.exe not found next to this script. Trying to download UE Viewer...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest 'https://github.com/gildor2/UEViewer/raw/master/umodel.exe' -OutFile '%HERE%umodel.exe' -UseBasicParsing } catch { exit 1 }"
  if not exist "%HERE%umodel.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest 'https://www.gildor.org/down/47/umodel/umodel_win32.zip' -OutFile '%HERE%umodel.zip' -UseBasicParsing; Expand-Archive -Force '%HERE%umodel.zip' '%HERE%umodel'; Remove-Item '%HERE%umodel.zip' } catch { exit 1 }"
  )
  if exist "%HERE%umodel\umodel.exe" set "UMODEL=%HERE%umodel\umodel.exe"
)
if not exist "%UMODEL%" (
  echo.
  echo  Could not get UModel automatically.
  echo  Download "UE Viewer" from https://www.gildor.org/en/projects/umodel, unzip it,
  echo  and put umodel.exe in this folder:  %HERE%
  echo  then run this script again.
  goto :fail
)

echo.
echo  Game folder : %GAME%
echo  UModel      : %UMODEL%
echo  Output      : %OUT%
echo.
if not exist "%OUT%" mkdir "%OUT%"

rem ---- whole packages (meshes, skeletons, textures, animations) ---------------------
for %%K in (CH_G2_Playable CH_HeadBobber ClassyGoat FeatherGoat Goast TallGoat ShoppingGoat Hats Heist_Masks_01 JetPack Longboard RocketBoots Heist_Camelgoat Heist_Dolphwheelgoat Heist_Flamingoat Heist_Handsomegoat Heist_PanzerGoat) do (
  echo  Exporting %%K ...
  "%UMODEL%" -path="%CONTENT%" -game=ue3 -out="%OUT%" -png -export %%K >nul 2>&1
  if errorlevel 1 echo     warning: %%K reported an error ^(some of its files may be missing^)
)

rem ---- the main Goat package crashes UModel when exported in one go, so do it per object --
echo  Exporting Goat ^(object by object^) ...
for %%O in (goat Gloria_01 GoatRipped) do "%UMODEL%" -path="%CONTENT%" -game=ue3 -out="%OUT%" -export Goat %%O SkeletalMesh3 >nul 2>&1
"%UMODEL%" -path="%CONTENT%" -game=ue3 -out="%OUT%" -export Goat Goat_Anim_01 AnimSet >nul 2>&1
for %%O in (Goat_Dif_01 Goat_Dif_02 Goat_Nor_01 Goat_White_Dif_01 Goat_Black_Dif_01 Goat_Brown_Dif_01 GoatRipped_Dif_01 GoatRipped_Nor_01 GoatEyes) do "%UMODEL%" -path="%CONTENT%" -game=ue3 -out="%OUT%" -png -export Goat %%O Texture2D >nul 2>&1

echo.
set "N=0"
for /f %%C in ('dir /s /b /a-d "%OUT%\*.psk" "%OUT%\*.pskx" 2^>nul ^| find /c /v ""') do set "N=%%C"
echo  Done. %N% model files are in:
echo    %OUT%
echo.
echo  Next: open Blender and run Blender\import_goats.py ^(see Docs\README.md^).
echo  Includes the PAYDAY heist masks (Heist_Masks_01). Known gaps: the Turban, CycleHelm and Hanglider assets do not export.
echo.
pause
exit /b 0

:fail
echo.
pause
exit /b 1
