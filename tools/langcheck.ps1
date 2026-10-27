param([Parameter(Mandatory = $true)][string]$Code)
# Vérifie lang\<code>.txt par rapport à lang\strings.txt : toutes les clés, dans l'ordre, mêmes {0}/{1}, mêmes \n,
# mêmes espaces en début et fin, mêmes motifs de filtre de fichier (*.csv)|*.csv.
$root = Split-Path $PSScriptRoot -Parent
$keys = [IO.File]::ReadAllLines((Join-Path $root 'lang\strings.txt'), [Text.Encoding]::UTF8) | ForEach-Object { ($_ -split "`t", 2)[0] }
$file = Join-Path $root "lang\$Code.txt"
if (-not (Test-Path $file)) { "MANQUANT : $file"; exit 1 }
$lines = [IO.File]::ReadAllLines($file, [Text.Encoding]::UTF8)
$errors = 0
function Err($m) { $script:errors++; if ($script:errors -le 40) { $m } }
if ($lines.Count -ne $keys.Count) { Err "nombre de lignes : $($lines.Count) au lieu de $($keys.Count)" }
$seen = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    $p = $lines[$i] -split "`t"
    if ($p.Count -ne 2) { Err "ligne $($i + 1) : il faut exactement une tabulation"; continue }
    $k = $p[0]; $t = $p[1]
    if ($i -lt $keys.Count -and $k -ne $keys[$i]) { Err "ligne $($i + 1) : clé modifiée ou déplacée [$k] attendu [$($keys[$i])]"; continue }
    $seen[$k] = $true
    if ($t.Trim() -eq '') { Err "ligne $($i + 1) : traduction vide [$k]" }
    $ph = { param($s) ([regex]::Matches($s, '\{\d\}') | ForEach-Object { $_.Value } | Sort-Object) -join ',' }
    if ((& $ph $k) -ne (& $ph $t)) { Err "ligne $($i + 1) : {0}/{1} différents [$k] -> [$t]" }
    if (([regex]::Matches($k, '\\n')).Count -ne ([regex]::Matches($t, '\\n')).Count) { Err "ligne $($i + 1) : nombre de \n différent [$k]" }
    if (($k -match '^\s') -ne ($t -match '^\s')) { Err "ligne $($i + 1) : espace de début [$k]" }
    if (($k -match '\s$') -ne ($t -match '\s$')) { Err "ligne $($i + 1) : espace de fin [$k]" }
    $pat = { param($s) ([regex]::Matches($s, '\|\*[\w.;*]*') | ForEach-Object { $_.Value }) -join '' }
    if ((& $pat $k) -ne (& $pat $t)) { Err "ligne $($i + 1) : motifs de filtre |*.ext modifiés [$k]" }
    if ($t -match '\\(?![n\\t])') { Err "ligne $($i + 1) : barre oblique inverse isolée [$t]" }
}
foreach ($k in $keys) { if (-not $seen.ContainsKey($k)) { Err "clé absente : [$k]" } }
if ($errors -eq 0) { "OK $Code : $($lines.Count) textes" } else { "$errors erreur(s) dans $Code"; exit 1 }
