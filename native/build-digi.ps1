# Compile la DLL des décodeurs numériques de SDRLink (32 bits, comme SDRLink.exe) avec LLVM-MinGW (tools\install-toolchain.ps1) :
# FT8 / FT4 / FT2 (ft8_lib, MIT) et Olivia / Contestia (bibliothèque MFSK de Pawel Jalocha, GPL), sources dans native\digi.
# Résultat : native\out\sdrlink_digi.dll et native\sdrlink_digi.dll.gz, à refaire seulement si les sources C/C++ changent (build.ps1 embarque le .gz).
param([string]$Opt = '-O2')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cc = Get-ChildItem (Join-Path $root 'toolchain') -Filter 'i686-w64-mingw32-clang.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $cc) { throw 'LLVM-MinGW absent : lancer tools\install-toolchain.ps1' }
$cxx = Join-Path $cc.DirectoryName 'i686-w64-mingw32-clang++.exe'
$s = Join-Path $PSScriptRoot 'digi'
$out = Join-Path $PSScriptRoot 'out'
$obj = Join-Path $out 'digi-obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$cfiles = @('sdrdec_ft8.c',
  'ft8lib/ft8/constants.c', 'ft8lib/ft8/crc.c', 'ft8lib/ft8/decode.c', 'ft8lib/ft8/encode.c', 'ft8lib/ft8/ldpc.c',
  'ft8lib/ft8/message.c', 'ft8lib/ft8/text.c', 'ft8lib/fft/kiss_fft.c', 'ft8lib/fft/kiss_fftr.c', 'ft8lib/common/monitor.c')
$inc = @(('-I' + $s), ('-I' + (Join-Path $s 'ft8lib')))
$common = @($Opt, '-msse2', '-mfpmath=sse', '-Wno-unused-command-line-argument')
$objs = @()
foreach ($f in $cfiles) {
  $o = Join-Path $obj (($f -replace '[/\\]', '_') -replace '\.c$', '.o')
  & $cc.FullName -c -std=c11 @common @inc (Join-Path $s $f) -o $o
  if ($LASTEXITCODE -ne 0) { throw "compilation de $f : code $LASTEXITCODE" }
  $objs += $o
}
$o = Join-Path $obj 'sdrdec_digi.o'
& $cxx -c -std=gnu++11 -fno-exceptions -fno-rtti @common @inc (Join-Path $s 'sdrdec_digi.cpp') -o $o
if ($LASTEXITCODE -ne 0) { throw "compilation de sdrdec_digi.cpp : code $LASTEXITCODE" }
$objs += $o
$dll = Join-Path $out 'sdrlink_digi.dll'
# -static : libc++ et le reste de l'exécution C++ dans la DLL (seules les DLL du système sont importées)
& $cxx -shared @objs -o $dll -static '-Wl,--kill-at' -s
if ($LASTEXITCODE -ne 0) { throw "édition de liens : code $LASTEXITCODE" }
"OK : $dll ({0:N0} octets)" -f (Get-Item $dll).Length
# Version compressée, gardée dans le dépôt : build.ps1 l'embarque sans avoir besoin du compilateur C.
$gz = Join-Path $PSScriptRoot 'sdrlink_digi.dll.gz'
$in = [IO.File]::OpenRead($dll)
$fs = [IO.File]::Create($gz)
$z = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
$in.CopyTo($z); $z.Dispose(); $fs.Dispose(); $in.Dispose()
"OK : $gz ({0:N0} octets)" -f (Get-Item $gz).Length
