# Compile SDRLink en un seul exe autonome, avec le compilateur C# fourni avec Windows (.NET Framework 4.x).
param([string]$Out = 'dist')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pkg  = Join-Path $root 'packages\webview2'
$bin  = Join-Path $root $Out
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'

if (-not (Test-Path $pkg)) {
    New-Item -ItemType Directory -Force (Join-Path $root 'packages') | Out-Null
    $zip = Join-Path $root 'packages\webview2.zip'
    Invoke-WebRequest -UseBasicParsing 'https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/1.0.2903.40' -OutFile $zip
    Expand-Archive -Force $zip $pkg
}

$lib    = Join-Path $pkg 'lib\net462'
$core   = Join-Path $lib 'Microsoft.Web.WebView2.Core.dll'
$wf     = Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $pkg 'runtimes\win-x86\native\WebView2Loader.dll'

New-Item -ItemType Directory -Force $bin | Out-Null
# Anciennes DLL posées à côté de l'exe : plus nécessaires.
Remove-Item (Join-Path $bin '*.dll') -ErrorAction SilentlyContinue

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 `
    "/out:$bin\SDRLink.exe" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$core" "/r:$wf" `
    "/resource:$core,Microsoft.Web.WebView2.Core.dll" `
    "/resource:$wf,Microsoft.Web.WebView2.WinForms.dll" `
    "/resource:$loader,WebView2Loader.dll" `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation" }
Write-Host "OK : $bin\SDRLink.exe (autonome)"
