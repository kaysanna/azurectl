param([string]$Executable=(Join-Path $PSScriptRoot '../publish/AzureCTL.exe'))
$ErrorActionPreference='Stop'
$Executable=[IO.Path]::GetFullPath($Executable)
$version=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo($Executable).FileVersion
$output=Join-Path (Split-Path $PSScriptRoot -Parent) ('release/v'+$version.ToString(3))
$null=New-Item -ItemType Directory -Path $output -Force
Copy-Item -LiteralPath $Executable -Destination (Join-Path $output 'AzureCTL.exe') -ErrorAction Stop
Get-FileHash -LiteralPath (Join-Path $output 'AzureCTL.exe') -Algorithm SHA256
