param([string]$DotnetPath='dotnet',[string]$Version='1.3.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid release version'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release=Join-Path $root 'release'
$artifacts=Join-Path $root 'artifacts'
$native=Join-Path $artifacts ('native-'+[Guid]::NewGuid().ToString('N'))
$stage=Join-Path $release ('stage-'+[Guid]::NewGuid().ToString('N'))
$app=Join-Path $stage 'DesktopIsland'
New-Item -ItemType Directory -Path $app,$native -Force | Out-Null
& $DotnetPath publish (Join-Path $root 'DesktopIsland.csproj') -c Release -r win-x64 --self-contained true -o $app -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Application publish failed'}
$collector=Join-Path $app 'collector'
& $DotnetPath publish (Join-Path $root 'Collector\DesktopIsland.FpsCollector.csproj') -c Release -r win-x64 --self-contained true -o $collector -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Collector publish failed'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if(-not (Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio Build Tools with Desktop development with C++.'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'C++ build tools were not found.'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$toolDir=Join-Path $app 'tools'
New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
$compile=Join-Path $native 'compile-native.cmd'
$probe=Join-Path $native 'frame-probe.exe'
$commands=@('@echo off',('call "'+$vcvars+'" >nul'),('cl /nologo /utf-8 /O2 /EHsc /MT "'+(Join-Path $root 'scripts\register-shortcut.cpp')+'" /Fo"'+(Join-Path $native 'shortcut.obj')+'" /Fe"'+(Join-Path $toolDir 'DesktopIsland.Shortcut.exe')+'"'),'if errorlevel 1 exit /b 1',('cl /nologo /utf-8 /O2 /EHsc /MT "'+(Join-Path $root 'tests\headless-frame-probe.cpp')+'" /Fo"'+(Join-Path $native 'frame-probe.obj')+'" /Fe"'+$probe+'"'),'exit /b %errorlevel%')
[IO.File]::WriteAllLines($compile,$commands,[Text.Encoding]::Default)
& $env:ComSpec /d /c $compile
if($LASTEXITCODE -ne 0){throw 'Native helper build failed'}
$scripts=Join-Path $app 'scripts'
New-Item -ItemType Directory -Path $scripts -Force | Out-Null
foreach($name in @('install-fps-task.ps1','remove-fps-task.ps1','uninstall.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $scripts}
foreach($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','Install.cmd','使用说明.html')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $app}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $app -Recurse
$hashes=@(Get-ChildItem -LiteralPath $collector -Recurse -File | Sort-Object FullName | ForEach-Object {@{Name=$_.FullName.Substring($collector.Length+1);Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $collector 'sha256.json') -Encoding UTF8
$files=@(Get-ChildItem -LiteralPath $app -Recurse -File | Sort-Object FullName | ForEach-Object {$_.FullName.Substring($app.Length+1)})
$files | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $app 'app-files.json') -Encoding UTF8
if(Get-ChildItem -LiteralPath $app -Recurse -Filter '*.pdb'){throw 'Debug symbols must not be included in the public package'}
$archive=Join-Path $release ('DesktopIsland-'+$Version+'-win-x64.zip')
Compress-Archive -LiteralPath $app -DestinationPath $archive -Force
$hash=Get-FileHash -LiteralPath $archive -Algorithm SHA256
($hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
@{Version=$Version;Directory=$app;Archive=$archive;Sha256=$hash.Hash;FrameProbe=$probe} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'package-result.json') -Encoding UTF8
Write-Output ('Package: '+$archive)
