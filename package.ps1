param([Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$IsccPath)
$ErrorActionPreference='Stop'
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $destination){throw 'Choose a new, empty output directory.'}
$payload=Join-Path $destination 'CodexSettings-Portable-v7.2.7'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath (Join-Path $payload 'CodexSettings.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.md'),(Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $PSScriptRoot 'docs\Inno-LICENSE.txt') -Destination $payload
New-Item -ItemType Directory -Path (Join-Path $payload 'accounts\life'),(Join-Path $payload 'accounts\work') -Force | Out-Null
$manifest=Get-ChildItem -LiteralPath $payload -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower()+'  '+$_.Name }
[IO.File]::WriteAllLines((Join-Path $payload 'SHA256SUMS.txt'),$manifest,[Text.UTF8Encoding]::new($false))
& $IsccPath "/DPayloadDir=$payload" "/DOutputDirPath=$destination" (Join-Path $PSScriptRoot 'installer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload,(Join-Path $destination 'CodexSettings-Portable-v7.2.7.zip'),[IO.Compression.CompressionLevel]::Optimal,$true,[Text.Encoding]::UTF8)
$hashes=Get-ChildItem -LiteralPath $destination -File | Where-Object { $_.Extension -in '.zip','.exe' } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower()+'  '+$_.Name }
[IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
