param([string]$Executable=(Join-Path $PSScriptRoot '../publish/AzureCTL.exe'))
$ErrorActionPreference='Stop'
$Executable=[IO.Path]::GetFullPath($Executable)
$version=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo($Executable).FileVersion
$tag='v'+$version.ToString(3)
$root=Split-Path $PSScriptRoot -Parent
$commit=git -c "safe.directory=$root" -C $root rev-parse HEAD
if($LASTEXITCODE -ne 0){throw 'Cannot determine release commit'}
$token=$env:GITHUB_TOKEN
if(-not $token){
 $lines="protocol=https`nhost=github.com`nusername=kaysanna`n`n"|git -c credential.interactive=never credential fill
 if($LASTEXITCODE -ne 0){throw 'GitHub authentication is required'}
 $passwordLine=$lines|Where-Object {$_ -like 'password=*'}|Select-Object -First 1
 if($passwordLine){$token=$passwordLine.Substring(9)}
}
if(-not $token){throw 'GitHub authentication is required'}
$headers=@{Authorization=('Bearer '+$token);Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='AzureCTL-release'}
$api='https://api.github.com/repos/kaysanna/azurectl/releases'
try{
 $releases=@();$page=1
 do{$batch=Invoke-RestMethod ($api+'?per_page=100&page='+$page) -Headers $headers;$releases+=@($batch);$page++}while($batch.Count -eq 100)
 foreach($existing in $releases){
  if(-not $existing.draft -and -not $existing.prerelease){
   $other=[Version]($existing.tag_name.TrimStart('v'))
   if($other -gt $version){throw 'A newer version is already published; refusing to replace it'}
  }
 }
 $release=$releases|Where-Object {$_.tag_name -eq $tag}|Select-Object -First 1
 if(-not $release){
  $body=@{tag_name=$tag;target_commitish=$commit.Trim();name=('AzureCTL '+$version.ToString(3));draft=$true;prerelease=$false;body='Download AzureCTL.exe and run it. No installer or companion files required. Automatic updates use the EXE directly and preserve your settings and server saves.'}|ConvertTo-Json
  $release=Invoke-RestMethod $api -Method Post -Headers $headers -ContentType 'application/json' -Body $body
 }
 $asset=@($release.assets)|Where-Object {$_.name -eq 'AzureCTL.exe'}|Select-Object -First 1
 if(-not $asset){$upload=($release.upload_url -split '\{')[0]+'?name=AzureCTL.exe';$asset=Invoke-RestMethod $upload -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $Executable}
 $hash=(Get-FileHash -LiteralPath $Executable).Hash.ToLowerInvariant()
 if($asset.size -ne (Get-Item -LiteralPath $Executable).Length -or $asset.digest -ne ('sha256:'+$hash)){throw 'Uploaded EXE verification failed; no old release was deleted'}
 $release=Invoke-RestMethod ($api+'/'+$release.id) -Headers $headers
 if(@($release.assets).Count -ne 1 -or $release.assets[0].name -ne 'AzureCTL.exe'){throw 'Expected exactly one EXE asset; no old release was deleted'}
 if($release.draft){$release=Invoke-RestMethod ($api+'/'+$release.id) -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'}
 $latest=Invoke-RestMethod ($api+'/latest') -Headers $headers
 if($latest.id -ne $release.id){throw 'Latest release feed does not match; no old release was deleted'}
 foreach($old in $releases){
  if($old.id -ne $release.id -and -not $old.draft -and $old.published_at -le $release.published_at){
   Invoke-RestMethod ($api+'/'+$old.id) -Method Delete -Headers $headers|Out-Null
   Write-Output ('Removed older release '+$old.tag_name+'; Git tag retained')
  }
 }
 Write-Output ('Published '+$release.html_url+' with AzureCTL.exe only')
}finally{$headers.Authorization=$null;$token=$null;$lines=$null;$passwordLine=$null}
