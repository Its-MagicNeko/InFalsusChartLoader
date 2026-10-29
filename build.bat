@echo off
setlocal
cd /d "%~dp0"

rem Usage: build.bat [Release^|Debug] [--MelonLoader^|--BepInEx]
rem Defaults: Release and MelonLoader.
set CONFIG=Release
set LOADER=MelonLoader

:parse
if "%~1"=="" goto :parsed
set "KNOWN=0"
if /i "%~1"=="Release" ( set "KNOWN=1" & set "CONFIG=Release" )
if /i "%~1"=="Debug" ( set "KNOWN=1" & set "CONFIG=Debug" )
if /i "%~1"=="--MelonLoader" ( set "KNOWN=1" & set "LOADER=MelonLoader" )
if /i "%~1"=="--BepInEx" ( set "KNOWN=1" & set "LOADER=BepInEx" )
if "%KNOWN%"=="0" goto :badarg
shift
goto :parse

:parsed
set BUILD_CONFIG=%CONFIG%
if /i "%LOADER%"=="BepInEx" set BUILD_CONFIG=BepInEx%CONFIG%

rem Warnings are errors, and a rebuild makes the analyzers run.
set WARN=-warnaserror -t:Rebuild

rem Pause at the end unless CI is set.
set PAUSE=pause
if defined CI set PAUSE=

where dotnet >nul 2>&1 || goto :nodotnet

echo ============================================================
echo  InFalsusChartLoader - %CONFIG% build (%LOADER%)
echo  %CD%
echo ============================================================
echo.

dotnet build InFalsusChartLoader.csproj -c %BUILD_CONFIG% --nologo %WARN% -p:Loader=%LOADER%
if errorlevel 1 goto :failed

if /i "%CONFIG%"=="Release" goto :checkartifact
goto :built

:checkartifact

echo.
echo Checking that the Release build carries the function only...

rem ---------------------------------------------------------------------------
rem Release artifact check: the artifact must carry the shipping strings only.
rem ---------------------------------------------------------------------------
set "PSF=%TEMP%\InFalsusChartLoader_artifact_check_%BUILD_CONFIG%.ps1"
> "%PSF%" echo $ErrorActionPreference='Stop';
>> "%PSF%" echo $p='bin\%BUILD_CONFIG%\InFalsusChartLoader.dll';
>> "%PSF%" echo if(-not (Test-Path $p)){ Write-Host ('No artifact at '+$p); exit 1 };
>> "%PSF%" echo $b=[IO.File]::ReadAllBytes($p); $fail=0;
>> "%PSF%" echo $bad=@('charts folder: ','decoded ','streaming assets: ','interop types by name',' hooked','could not be resolved','note records measure','chart folder ','the pack list has','is not an array','has no element class','class pointer could not be read','probe: ','--- before ---','--- after ---','charts folder exists?','kept ','packToAssets    ','readings=','chart[0]','audio name ','audio read index ','is not ours','jacket asked for','jacket load claimed','read bookkeeping reached','outstanding-read sets','outstanding=','noread=','a call into the game','s rendering types raised','is missing','pack screen');
>> "%PSF%" echo $good=@('InFalsusChartLoader loading...','charts: ','offsets asked=','hooks installed','GameAssembly.dll','is already the name of a song this game');
>> "%PSF%" echo foreach($s in $bad){ $n=[Text.Encoding]::Unicode.GetBytes($s); $h=0;
>> "%PSF%" echo   for($i=0;$i -le $b.Length-$n.Length;$i++){ $ok=$true;
>> "%PSF%" echo     for($j=0;$j -lt $n.Length;$j++){ if($b[$i+$j] -ne $n[$j]){ $ok=$false; break } };
>> "%PSF%" echo     if($ok){ $h++ } };
>> "%PSF%" echo   if($h -gt 0){ Write-Host ('  FAIL  '+$s+'   found '+$h); $fail=1 }
>> "%PSF%" echo   else { Write-Host ('  ok    '+$s) } };
>> "%PSF%" echo foreach($s in $good){ $n=[Text.Encoding]::Unicode.GetBytes($s); $h=0;
>> "%PSF%" echo   for($i=0;$i -le $b.Length-$n.Length;$i++){ $ok=$true;
>> "%PSF%" echo     for($j=0;$j -lt $n.Length;$j++){ if($b[$i+$j] -ne $n[$j]){ $ok=$false; break } };
>> "%PSF%" echo     if($ok){ $h++ } };
>> "%PSF%" echo   if($h -eq 0){ Write-Host ('  FAIL  '+$s+'   missing'); $fail=1 }
>> "%PSF%" echo   else { Write-Host ('  ok    '+$s+'   '+$h) } };

rem ---------------------------------------------------------------------------
rem Every UTF-16 literal of 12 characters or more must be on the allow list.
rem ---------------------------------------------------------------------------
>> "%PSF%" echo $str=New-Object 'System.Collections.Generic.HashSet[string]';
>> "%PSF%" echo foreach($o in 0,1){ $c=New-Object System.Text.StringBuilder; for($i=$o;$i -lt $b.Length-1;$i+=2){ $n=$b[$i] + ($b[$i+1] * 256);
>> "%PSF%" echo   if($n -ge 32 -and $n -lt 127){ [void]$c.Append([char]$n) } else { if($c.Length -ge 12){ [void]$str.Add($c.ToString()) }; [void]$c.Clear() } }
>> "%PSF%" echo   if($c.Length -ge 12){ [void]$str.Add($c.ToString()) } };
>> "%PSF%" echo $allow=@(' could not be read: ',' has filter ',' hooks installed',' record(s) filed under this build''s song ids',' unresolved=',''' difficulty ',''' is already the name of a song this game ',''' was left out: ',''' was not loaded. Change the id ','^<exception could not be read: ','^<exception whose class could not be named^>','^<exception with no class^>','AddressableHandleAutoReleaser','AddressableHandleAutoReleaser._MIA could not be found; custom jackets will not be substituted onto the song list','Assembly Version','DobbyDestroy','Dictionary.KeyCollection','Dictionary.ValueCollection','FileDescription','Game.Common.dll','GameAssembly.dll','GameplayBackgrounds','InFalsusChartLoader','InFalsusChartLoader loading...','InFalsusChartLoader.dll','InternalName','LegalCopyright','GetCurrentLocalization','get_gameObject','MakeGenericMethod','NativeClassPtr','NativeFieldInfoPtr_','OriginalFilename','PackOutlineMember','PackSongCardMember','PackVisualMemberLarge','PackSelectScrollContainer','PackVisualMemberSmall','topScrollContainer','bottomScrollContainer','ProductVersion','SongData could not be found; the new songs will not show','SongData._yOA could not be found; the new songs will not show','Sprites/Default','StringFileInfo','StringTypeMapping','SupportsTextureFormat','TextureFormat','UnityEngine.','UnityEngine.CoreModule','UnityEngine.CoreModule.dll','Unlit/Texture','VS_VERSION_INFO','chart loading failed: ','detour faulted, custom charts disabled: ','ifapp.Game.Common','ifapp.Game.Scenes','in its if file.','no custom song could have its audio registered','no zlib data','offsets asked=','preview_seconds','registering the custom songs failed: ','set_mainTexture','the custom songs could not be added to the game''s song list','the game''s asset manager could not be reached; no custom audio can be registered, so no custom song can be played','the game''s asset manager would not take another file','the game''s chart decoder could not be reached; no charts will load','the game''s song list refresh raised: ','SongInfo.Size','SongChartInfo.Size','PackInfo.Size','SongChartInfo','SongSelectScene','DynamicStringMapping','PackSelectSceneAssets','packToAssets','packIdTypeMapping','songIdTitleTypeMapping','songIdArtistTypeMapping','jacketIllustratorNameTypeMapping','ArtistReadingOverride','LocalizationToTitleReadingOverride','PreviewStartSeconds','PreviewEndSeconds','DisplayChartDesigner','DisplayJacketDesigner','LevelSectionIndicator',': no method by that name in this build','the custom pack could not be added; the custom songs are in the song list but have no pack of their own','the custom pack could not be prepared while no charts are loaded; a chart added in the song select will not show its category until the scene is entered again','the pack row accessor could not be located in this build; the custom pack will wear whatever this build puts in its row','the game''s pack row getter could not be found by name in this build; the custom pack will wear whatever this build puts in its row','SingleFileSaveDataV2','GameResultsV4','GameResultKey','GameResultKey.Size','GameResultV4','GameResultsV4.highscores','no self-test (the result record''s size could not be asked of the game)','MemoryPackSerializer','MemoryPack.MemoryPackSerializer: no class to ask in this build; the custom results file cannot be read','MemoryPack.dll','CreateDefault','mscorlib.dll','TryGetResult','IFCL_temp.sav','custom results: ','no save object','no container','the save method is not on the container in this build','the game''s save could not be reached; the custom songs'' scores will not be read or saved','the save folder could not be read; the custom songs'' scores will not be read or saved','the custom results file exists but could not be read, so it was left alone; the custom songs'' scores will not be read or saved','the custom results could not be prepared; the custom songs'' scores will not be read or saved: ','the custom results'' stored song ids could not be rewritten, so scores recorded under an earlier build''s ids will not be found: ','the custom results file could not be written; this session''s custom scores are not saved: ','GameResultsV4.TryGetResult/TryUpdate could not be found in this build; custom scores will go into the game''s own save file','IFCL self test','no self-test (the record reader could not be found)','the table failed its self-test: ','the self-test could not be run: ','EncounterResults','UpdateEncounterResult','EncounterResults.UpdateEncounterResult could not be found in this build; a custom song''s encounter result will be written into the game''s own save',': no field by that name in this build',' could not be measured off the live arrays in this build: ','the game''s song and pack tables could not be read by name in this build; no custom song will be registered','the game''s data assets could not be found by name in this build; no custom song will be registered','SongSelectScene''s payload could not be read by name in this build; which difficulty is selected will not be known, so a song with a picture per difficulty will not show one','the read bookkeeping''s fields could not be read by name in this build; a served read will not release its block','the note record''s fields could not be read by name in this build; no chart can be checked, so none will load','the note records'' stride could not be measured in this build; no chart can be checked, so none will load','the pack''s card fields could not be read by name in this build; the cards will keep the difficulty they were first built with',': no usable field by that name in this build','StringTypeMapping.Mapping could not be found on its own class in this build; the custom pack''s texts will not be written','the pack''s fields could not be read by name in this build; the custom pack keeps the game''s own visuals and text','the array holds ',' element(s) in ','there is no array to measure','SongSelectPackAssets','PackSelectLargeNonCompleted','PackSelectNonCompleted',': no class to ask in this build',': no single class to ask in this build',': the generic instantiation is not in this build','the save''s difficulty could not be read by name in this build; which difficulty is selected will not be known before the song select has been visited',
>> "%PSF%" echo 'AddComponent','DontDestroyOnLoad','get_isPlaying','get_isPrepared','set_audioOutputMode','set_isLooping','set_playOnAwake','set_renderMode','set_targetTexture','RenderTexture','UnityEngine.Video','UnityEngine.VideoModule.dll','GeneralSaveState','GeneralSaveStateV5','LastSelectedDifficulty','InFalsusChartLoader background video','no custom song could be registered','the song table does not index the way this build expects ',' carries id ','); no custom song was ','registered, because their names cannot be checked against the game''s','get_mainTexture','; this mod''s background substitution is off for this session','Material.get_mainTexture could not be found','Material.set_mainTexture could not be found','Material.set_mainTexture raised: ','Material''s copy constructor could not be found','no clip material is loaded','the background material could not be cloned from the game''s own: ','the clone material could not be created','the clone returned but has no native object','the game''s own material did not load','the material''s main texture could not be read','there is no material to clone','the game''s empty-chart constructor is not in this build; a chart that fails to decode cannot be served','the custom pack has no row of visuals of its own; it will wear whatever this build draws for it','the background video could not be started with the song; the background will not move with it','the background video could not be placed on the chart clock; it will run out of step with the chart','the chart''s clock could not be read by name in this build; the in-play background video will not play','_NH._UoA: no method by that name in this build; a custom song''s difficulties will follow the game''s own locks, and a fresh save may leave them locked','DifficultyUnlockContainer',': no enum member by that name in this build','the results screen''s unlock progress will keep showing for custom songs',''' was left out: its audio could not be registered',''' was left out: the audio file could not be read',''' was not loaded','charts reloaded: +','the chart reload failed: ','the game''s asset manager is not ready; the charts were not reloaded','the custom pack is no longer in the pack list; its song list was not updated','the audio a finished scene switch should have released could not be cleared: ','no pack setter in this build','the empty pack could not be deselected; re-entering the song select with it selected will fail: ','the save object is not there','the song list could not be rebuilt','the song select''s refresh methods are not in this build; a reloaded chart will not be redrawn until the scene is entered again','the current pack could not be read; the song list was not rebuilt','packSelectLayer','PackSelectScene','PackSelectScrollContainer','UIScrollListView','contentContainer','the pack layer could not be read; the song list was not rebuilt','leftSongCardContainer','rightSongCardContainer','SongSelectSmallCardScrollContainer','the song cards could not be refreshed: ','the current pack could not be rebuilt; the song list was not refreshed','the category screen''s cards could not be rebuilt; it will keep the pictures it was built with','the category screen''s cards could not be rebuilt: ','the pack switch''s refresh is not in this build; a switched pack will keep the songs it was built with','the pack list''s counts could not be refreshed in this build; a reloaded pack will keep the counts it was first shown with','the pack list''s counts could not be refreshed: ','the hub''s song counts could not be refreshed: ','the current pack could not be rebuilt: ','the song list could not be refilled: ','the song list could not be cleared: ','the song list could not be sorted','the song could not be selected','the song select could not be refreshed','the pack list is not in this build; an empty custom pack will keep its category entry','the song select''s pack title could not be updated in this build; it will keep the name of the pack that left','packSelectButtonContainer','PackSelectButtonContainer','the song select''s pack title could not be updated in this build; it will keep the name of the pack that left ','Constrained2D','ifapp.Game.UI.Common','the empty custom pack could not be left out of the category list; it will appear until the scene is entered again','the category screen could not be rebuilt in this build; it will keep the packs it was built with','the category screen could not be rebuilt in this build; it will keep the packs it was built with ','the category screen''s position could not be held across its rebuild: ','the category screen could not be initialised');
>> "%PSF%" echo $arr=@($str); [Array]::Sort($arr); $off=0;
>> "%PSF%" echo foreach($s in $arr){ if($allow -notcontains $s){ Write-Host ('  OFF-LIST  '+$s); $off++ } };
>> "%PSF%" echo if($off -ne 0){ Write-Host ('  '+$off+' literal(s) in the artifact are on no list - a release should carry none of them.'); $fail=1 }
>> "%PSF%" echo else { Write-Host ('  ok    all '+$str.Count+' literal(s) of 12+ characters are accounted for') };
>> "%PSF%" echo if($fail -ne 0){ Write-Host 'The Release artifact carries a diagnostic string, or is missing one it should have.'; exit 1 };
>> "%PSF%" echo Write-Host 'Passed: the Release build carries the function only.';

powershell -NoProfile -ExecutionPolicy Bypass -File "%PSF%"
if errorlevel 1 set "CHECKFAILED=1"
del "%PSF%" >nul 2>&1
if defined CHECKFAILED goto :checkfailed

:built
echo.
echo Build succeeded.
echo Output: bin\%BUILD_CONFIG%\InFalsusChartLoader.dll
echo.
echo Nothing was copied into the game. Installing the DLL is a separate,
if /i "%LOADER%"=="BepInEx" (
  echo explicit step - put it in the game's BepInEx\plugins folder yourself, with the Charts
  echo folder at the game root.
) else (
  echo explicit step - put it in the game's Mods folder yourself, with the Charts
  echo folder beside the mod folder it lands in.
)
echo.
%PAUSE%
exit /b 0

:badarg
echo ERROR: unknown argument: %~1
echo.
echo Usage: build.bat [Release^|Debug] [--MelonLoader^|--BepInEx]
echo.
%PAUSE%
exit /b 1

:nodotnet
echo ERROR: dotnet was not found on PATH.
echo.
echo Install the .NET SDK - the project targets net6.0.
echo.
%PAUSE%
exit /b 1

:failed
echo.
echo BUILD FAILED - see the errors above.
echo.
%PAUSE%
exit /b 1

:checkfailed
echo.
echo BUILD FAILED - the Release artifact carries a diagnostic string, or is
echo missing one it should have. See the FAIL lines above.
echo.
%PAUSE%
exit /b 1
