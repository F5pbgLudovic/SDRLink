# Installe LLVM-MinGW (compilateur C de M. Storsjö, licence Apache 2.0) dans toolchain\, pour native\build-freedv.ps1.
# Seulement utile pour recompiler la DLL FreeDV : build.ps1 se sert de native\sdrlink_freedv.dll.gz, déjà dans le dépôt.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$name = 'llvm-mingw-20261104-ucrt-x86_64'
$sha = '552191b5754c49a5a20723276a6fc56541ea1922c4c50b7b669022333fc1b40f'
$dir = Join-Path $root 'toolchain'
if (Test-Path (Join-Path $dir "$name\bin\i686-w64-mingw32-clang.exe")) { "Déjà installé : $dir\$name"; return }
New-Item -ItemType Directory -Force (Join-Path $dir 'dl') | Out-Null
$zip = Join-Path $dir "dl\$name.zip"
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -UseBasicParsing "https://github.com/mstorsjo/llvm-mingw/releases/download/20261104/$name.zip" -OutFile $zip
if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() -ne $sha) { throw 'Empreinte SHA-256 incorrecte' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $dir)
"OK : $dir\$name"
