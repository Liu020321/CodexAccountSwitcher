param([switch]$Portable = $true, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'CodexSettings.exe' }
$defines = if ($Portable) { '/define:PORTABLE' } else { '/define:LOCAL' }
$base = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Management.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ "/out:$OutputPath" $defines "/win32icon:$base\app.ico" "/win32manifest:$base\app.manifest" "/resource:$base\Window.xaml,Window.xaml" @refs "$base\Core.cs" "$base\App.cs" "$base\Tests.cs" "$base\Monitor.cs" "$base\Theme.cs" "$base\Chrome.cs" "$base\Settings.cs" "$base\Transaction.cs" "$base\NativeServices.cs" "$base\QuotaIcon.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
[IO.File]::WriteAllText($OutputPath + '.config', '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
Write-Host "已生成 $OutputPath"
