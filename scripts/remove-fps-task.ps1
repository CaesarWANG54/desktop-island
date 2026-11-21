param([Parameter(Mandatory=$true)][string]$UserSid)
$ErrorActionPreference='Stop'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator approval is required to remove the protected collector.'}
if($UserSid -notmatch '^S-1-[0-9-]+$' -or [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $UserSid){throw 'Invalid user SID'}
$null=New-Object Security.Principal.SecurityIdentifier $UserSid
$programRoot=[IO.Path]::GetFullPath([Environment]::GetFolderPath('ProgramFiles')).TrimEnd('\')
$destination=[IO.Path]::GetFullPath((Join-Path $programRoot ('DesktopIsland\FpsCollector\'+$UserSid)))
if($destination -ne ($programRoot+'\DesktopIsland\FpsCollector\'+$UserSid)){throw 'Unsafe removal path'}
$service=New-Object -ComObject Schedule.Service
$service.Connect()
$folder=$service.GetFolder('\')
$task=$null
try{$task=$folder.GetTask('DesktopIsland-FPS-'+$UserSid)}catch{if($_.Exception.GetBaseException().HResult -ne -2147024894){throw}}
if($task){[void]$task.Stop(0);$folder.DeleteTask($task.Name,0)}
Start-Sleep -Seconds 2
if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Recurse -Force}
