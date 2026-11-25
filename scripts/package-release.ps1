param([string]$Executable=(Join-Path $PSScriptRoot '../publish/AzureCTL.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$Executable=[IO.Path]::GetFullPath($Executable)
$version=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo($Executable).FileVersion
$versionText=$version.ToString(3)
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root ('release/v'+$versionText)
$null=New-Item -ItemType Directory -Path $output -Force
$zip=Join-Path $output 'AzureCTL-win-x64.zip'
if(Test-Path -LiteralPath $zip){throw 'Release ZIP already exists. Use a new release version or review the existing package.'}
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try{$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$Executable,'AzureCTL.exe',[IO.Compression.CompressionLevel]::Optimal)}finally{$archive.Dispose()}
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest=@{version=$versionText;url=('https://github.com/kaysanna/azurectl/releases/download/v'+$versionText+'/AzureCTL-win-x64.zip');sha256=$hash;size=(Get-Item -LiteralPath $zip).Length}
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $output 'AzureCTL-update.json'),($manifest|ConvertTo-Json),$utf8)
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $output 'AzureCTL-update.json') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'),($hash+'  AzureCTL-win-x64.zip'+[Environment]::NewLine+$manifestHash+'  AzureCTL-update.json'+[Environment]::NewLine),$utf8)
Write-Output ('Packaged AzureCTL '+$versionText+' in '+$output)
