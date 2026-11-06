# Compile la DLL FreeDV RADE de SDRLink (32 bits, comme SDRLink.exe) avec LLVM-MinGW (tools\install-toolchain.ps1).
# Résultat : native\out\sdrlink_freedv.dll, à recompiler seulement si les sources C changent (build.ps1 l'embarque dans l'exe).
param([string]$Opt = '-O2')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cc = Get-ChildItem (Join-Path $root 'toolchain') -Filter 'i686-w64-mingw32-clang.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $cc) { throw 'LLVM-MinGW absent : lancer tools\install-toolchain.ps1' }
$s = Join-Path $PSScriptRoot 'freedv'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$files = @('sdrdec_rade.c',
  'rade/rade_rx.c', 'rade/rade_dec.c', 'rade/rade_dec_data.c', 'rade/rade_dsp.c', 'rade/rade_ofdm.c', 'rade/rade_bpf.c', 'rade/rade_acq.c',
  'opus/dnn/fargan.c', 'opus/dnn/fargan_data.c', 'opus/dnn/nnet.c', 'opus/dnn/nnet_default.c', 'opus/dnn/parse_lpcnet_weights.c', 'opus/dnn/lpcnet_tables.c',
  'rade2/rade_v2_ofdm.c', 'rade2/rade_rx_v2.c', 'rade2/rade_dec_v2.c', 'rade2/rade_dec_v2_data.c', 'rade2/rade_sync.c', 'rade2/rade_sync_data.c') |
  ForEach-Object { Join-Path $s $_ }
$inc = @('.', 'opus', 'opus/dnn', 'opus/celt', 'opus/include', 'rade', 'rade2') | ForEach-Object { '-I' + (Join-Path $s $_) }
$dll = Join-Path $out 'sdrlink_freedv.dll'
& $cc.FullName -shared -std=c11 $Opt -msse2 -mfpmath=sse -DHAVE_CONFIG_H=1 -DSDRDEC_RADE=1 @inc @files -o $dll -static '-Wl,--kill-at' -s -Wno-unused-command-line-argument
if ($LASTEXITCODE -ne 0) { throw "compilation : code $LASTEXITCODE" }
"OK : $dll ({0:N0} octets)" -f (Get-Item $dll).Length
# Version compressée, gardée dans le dépôt : build.ps1 l'embarque sans avoir besoin du compilateur C.
$gz = Join-Path $PSScriptRoot 'sdrlink_freedv.dll.gz'
$in = [IO.File]::OpenRead($dll)
$fs = [IO.File]::Create($gz)
$z = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
$in.CopyTo($z); $z.Dispose(); $fs.Dispose(); $in.Dispose()
"OK : $gz ({0:N0} octets)" -f (Get-Item $gz).Length
