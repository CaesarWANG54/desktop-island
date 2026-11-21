param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$UserSid,[Parameter(Mandatory=$true)][string]$ClientPath)
$ErrorActionPreference='Stop'
trap {
 $failure=$_.ToString()+[Environment]::NewLine+$_.ScriptStackTrace
 if($destination -and (Test-Path -LiteralPath $destination)){$failure | Set-Content -LiteralPath (Join-Path $destination 'setup-error.txt') -Encoding UTF8}
 exit 1
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal $identity
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator approval is required for this one-time setup.'}
if($UserSid -notmatch '^S-1-[0-9-]+$' -or $identity.User.Value -ne $UserSid){throw 'Use the same Windows administrator account that runs Desktop Island.'}
$null=New-Object Security.Principal.SecurityIdentifier $UserSid
$client=[IO.Path]::GetFullPath($ClientPath)
if([IO.Path]::GetFileName($client) -ne 'DesktopIsland.exe' -or -not (Test-Path -LiteralPath $client) -or $client.StartsWith('\\')){throw 'Invalid application path'}
$sourceRoot=[IO.Path]::GetFullPath($Source).TrimEnd('\')
$programRoot=[IO.Path]::GetFullPath([Environment]::GetFolderPath('ProgramFiles')).TrimEnd('\')
$destination=[IO.Path]::GetFullPath((Join-Path $programRoot ('DesktopIsland\FpsCollector\'+$UserSid)))
if($destination -ne ($programRoot+'\DesktopIsland\FpsCollector\'+$UserSid)){throw 'Invalid destination'}
$manifest=Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'sha256.json') | ConvertFrom-Json
foreach($file in $manifest){
 $candidate=[IO.Path]::GetFullPath((Join-Path $sourceRoot $file.Name))
 if(-not $candidate.StartsWith($sourceRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid manifest entry'}
 if((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $file.Hash){throw 'Collector checksum mismatch'}
}
$taskName='DesktopIsland-FPS-'+$UserSid
$service=New-Object -ComObject Schedule.Service
$service.Connect()
$existing=$null
try {$existing=$service.GetFolder('\').GetTask($taskName)} catch {if($_.Exception.GetBaseException().HResult -ne -2147024894){throw}}
if($existing){
 [void]$existing.Stop(0)
 for($attempt=0;$attempt -lt 30 -and $existing.State -eq 4;$attempt++){Start-Sleep -Milliseconds 100}
 if($existing.State -eq 4){throw 'The previous collector did not stop.'}
}
$oldCollectors=Get-Process -Name DesktopIsland.FpsCollector -ErrorAction SilentlyContinue
foreach($old in $oldCollectors){
 if($old.Path -eq (Join-Path $destination 'DesktopIsland.FpsCollector.exe') -and -not $old.WaitForExit(5000)){throw 'The collector still holds its files.'}
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)')
Set-Acl -LiteralPath $destination -AclObject $acl
foreach($file in $manifest){
 $target=[IO.Path]::GetFullPath((Join-Path $destination $file.Name))
 if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid target'}
 New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $sourceRoot $file.Name) -Destination $target -Force
 if(([IO.File]::GetAttributes($target) -band [IO.FileAttributes]::Encrypted) -ne 0){[IO.File]::Decrypt($target)}
 if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Hash){throw 'Installed checksum mismatch'}
 $fileAcl=Get-Acl -LiteralPath $target
 $fileAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
 Set-Acl -LiteralPath $target -AclObject $fileAcl
}
$version=(Get-Item -LiteralPath $client).VersionInfo.FileVersion
$version=([version]$version).ToString(3)
@{UserSid=$UserSid;ClientPath=$client;Version=$version} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'client.json') -Encoding UTF8
$definition=$service.NewTask(0)
$definition.RegistrationInfo.Description='DesktopIsland external DXGI frame counter. No game injection. Starts on demand; stops tracing when disconnected.'
$definition.Principal.UserId=$UserSid
$definition.Principal.LogonType=3
$definition.Principal.RunLevel=1
$definition.Settings.Enabled=$true
$definition.Settings.AllowDemandStart=$true
$definition.Settings.DisallowStartIfOnBatteries=$false
$definition.Settings.StopIfGoingOnBatteries=$false
$definition.Settings.ExecutionTimeLimit='PT0S'
$definition.Settings.MultipleInstances=2
$definition.Settings.Priority=7
$action=$definition.Actions.Create(0)
$action.Path=Join-Path $destination 'DesktopIsland.FpsCollector.exe'
$action.WorkingDirectory=$destination
$taskAcl='O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;'+$UserSid+')'
$task=$service.GetFolder('\').RegisterTaskDefinition($taskName,$definition,6,$UserSid,$null,3,$taskAcl)
[void]$task.Run($null)
@{InstalledAt=(Get-Date -Format o);TaskName=$taskName;Executable=$action.Path;Version=$version} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'installation.json') -Encoding UTF8
