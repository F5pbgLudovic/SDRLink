# Produit src\LangData.cs à partir des traductions lang\<code>.txt (texte anglais <TAB> traduction, une ligne par texte).
# À relancer après avoir modifié une traduction ; lang\strings.txt se régénère avec tools\LangExtract.cs.
$root = Split-Path $PSScriptRoot -Parent
$sb = New-Object Text.StringBuilder
[void]$sb.Append(@'
using System.Collections.Generic;

namespace SDRLink
{
    // Traductions (texte anglais -> langue). Fichier généré par tools\langgen.ps1 à partir de lang\*.txt : ne pas modifier à la main.
    static class LangData
    {
        public static Dictionary<string, string> Load(string code)
        {
            string[] p;
            switch (code)
            {

'@)
$codes = @()
foreach ($f in Get-ChildItem (Join-Path $root 'lang') -Filter '*.txt' | Sort-Object Name) {
    if ($f.Name -eq 'strings.txt') { continue }
    $code = $f.BaseName
    $codes += $code
    [void]$sb.Append("                case `"$code`": p = $($code.ToUpper()); break;`r`n")
}
[void]$sb.Append(@'
                default: return new Dictionary<string, string>();
            }
            Dictionary<string, string> d = new Dictionary<string, string>(p.Length / 2);
            for (int i = 0; i + 1 < p.Length; i += 2) d[p[i]] = p[i + 1];
            return d;
        }

'@)
foreach ($code in $codes) {
    [void]$sb.Append("        static readonly string[] $($code.ToUpper()) =`r`n        {`r`n")
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $root "lang\$code.txt"), [Text.Encoding]::UTF8)) {
        $p = $line -split "`t", 2
        if ($p.Count -ne 2) { continue }
        # Les fichiers utilisent déjà les échappements C# (\n, \t, \\) : seuls les guillemets restent à protéger.
        $k = $p[0].Replace('"', '\"'); $t = $p[1].Replace('"', '\"')
        [void]$sb.Append("            `"$k`", `"$t`",`r`n")
    }
    [void]$sb.Append("        };`r`n`r`n")
}
[void]$sb.Append("    }`r`n}`r`n")
[IO.File]::WriteAllText((Join-Path $root 'src\LangData.cs'), $sb.ToString(), (New-Object Text.UTF8Encoding $true))
"src\LangData.cs : $($codes.Count) langue(s) : $($codes -join ', ')"
