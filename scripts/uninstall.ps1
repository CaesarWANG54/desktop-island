param([switch]$RemoveData)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$expected=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\DesktopIsland')).TrimEnd('\')
if($root -ne $expected){throw 'Run this uninstaller from the installed application directory.'}
$exe=Join-Path $root 'DesktopIsland.exe'
if(Test-Path -LiteralPath $exe){& $exe --control quit;Start-Sleep -Seconds 3}
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$collector=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) ('DesktopIsland\FpsCollector\'+$sid)
if(Test-Path -LiteralPath $collector){
 $script=Join-Path $PSScriptRoot 'remove-fps-task.ps1'
 $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$script+'" -UserSid '+$sid
 $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
 $process.WaitForExit()
 if($process.ExitCode -ne 0){throw 'Collector removal was cancelled or failed. Application files were kept.'}
}
$shell=New-Object -ComObject Shell.Application
foreach($folder in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))){
 $link=Join-Path $folder 'Desktop Island.lnk'
 if((Test-Path -LiteralPath $link) -and $shell.NameSpace($folder).ParseName('Desktop Island.lnk').GetLink.Path -eq $exe){Remove-Item -LiteralPath $link}
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name DesktopIsland -ErrorAction SilentlyContinue
foreach($key in @('HKCU:\Software\Classes\AppUserModelId\DesktopIsland','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopIsland')){
 if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Recurse}
}
if($RemoveData){
 $data=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DesktopIsland'))
 if($data -ne ([IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')+'\DesktopIsland')){throw 'Invalid data path'}
 if(Test-Path -LiteralPath $data){Remove-Item -LiteralPath $data -Recurse -Force}
}
# Both root and data are fixed, checked application directories; no wildcard paths.
Remove-Item -LiteralPath $root -Recurse -Force
Write-Output 'Desktop Island uninstalled. Settings are retained unless -RemoveData was supplied.'
