param([string[]]$PrivateMarkers=@())
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files=if(Test-Path -LiteralPath (Join-Path $root '.git')){& git -C $root ls-files}else{Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {$_.FullName -notmatch '\\(bin|obj|release|artifacts|\.git|tools)\\'} | ForEach-Object {$_.FullName.Substring($root.Length+1)}}
$rules=@{
 'credential token'='(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|sk-(?:proj-)?[A-Za-z0-9_-]{30,}|AIza[0-9A-Za-z_-]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)'
 'hardcoded Windows user path'='[A-Za-z]:\\Users\\(?!Public(?:\\|$)|Default(?:\\|$)|Example(?:\\|$)|YourName(?:\\|$))[^\\\s"\x27]+'
 'hardcoded machine SID'='S-1-5-21-\d+-\d+-\d+-\d+'
}
$failures=0
foreach($name in $files){
 if($name -match '(^|[/\\])(settings|timers|diagnostics|probe|client|installation|weather-cache)\.json$' -or $name -match '\.(pfx|p12|key|pem|log|etl|dmp|lrc|pdb)$'){
  Write-Output ('Private/generated file in source: '+$name);$failures++;continue
 }
 if([IO.Path]::GetExtension($name) -notin @('.cs','.csproj','.json','.md','.ps1','.cmd','.cpp','.xaml','.manifest','.yml','.yaml','.html','.txt','.svg','')){continue}
 $content=[IO.File]::ReadAllText((Join-Path $root $name))
 foreach($rule in $rules.GetEnumerator()){if([regex]::IsMatch($content,$rule.Value)){Write-Output ($rule.Key+': '+$name);$failures++}}
 foreach($marker in $PrivateMarkers){if($marker.Length -gt 0 -and $content.IndexOf($marker,[StringComparison]::OrdinalIgnoreCase) -ge 0){Write-Output ('Private marker in: '+$name);$failures++}}
}
if($failures -gt 0){throw ('Privacy check found '+$failures+' item(s). Review locally; never print matched secret values.')}
Write-Output ('Privacy check passed: '+@($files).Count+' source files.')
