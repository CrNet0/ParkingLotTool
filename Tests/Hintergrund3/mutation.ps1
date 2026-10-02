$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$quelle = Join-Path $repo 'Geometry/ExklusivesBaubild.cs'
$original = [IO.File]::ReadAllText($quelle)
$zeile = 'return spielBereit && definitionen == 0 && temps == 0;'
if (($original.Split(@($zeile), [StringSplitOptions]::None).Length - 1) -ne 1) {
    throw 'Mutationsstelle geaendert: Pruefer aktualisieren.'
}
$mutiert = Join-Path $PSScriptRoot 'Zulassung.mutiert.txt'
$projekt = Join-Path $PSScriptRoot 'Hintergrund3.csproj'
$ablage = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $ablage -Force | Out-Null
$mutationen = @(
    @{ Name = 'fremde-temps-zulassen'; Ersatz = 'return spielBereit && (definitionen == 0 || temps == 0);'; Fehler = 4 },
    @{ Name = 'gueltigen-bau-verweigern'; Ersatz = 'return false;'; Fehler = 1 }
)
try {
    $env:PLT_NUR_ZULASSUNG = '1'
    foreach ($mutation in $mutationen) {
        [IO.File]::WriteAllText($mutiert, $original.Replace($zeile, $mutation.Ersatz))
        $ausgabe = & dotnet run -c Release --project $projekt "-p:Zulassungsquelle=$mutiert" 2>&1
        $code = $LASTEXITCODE
        $log = Join-Path $ablage ($mutation.Name + '.log')
        $ausgabe | Set-Content -LiteralPath $log -Encoding utf8
        $gemeldet = @($ausgabe | Where-Object { "$_" -match '^FEHLER:' }).Count
        if ($code -ne 1 -or $gemeldet -ne $mutation.Fehler) {
            throw "Mutation $($mutation.Name): Exit $code, Fehler $gemeldet; erwartet 1/$($mutation.Fehler)."
        }
        Write-Output "Mutation $($mutation.Name): Exit $code, $gemeldet Fehler (erwartet)."
    }
}
finally {
    Remove-Item Env:PLT_NUR_ZULASSUNG -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $mutiert -ErrorAction SilentlyContinue
    # Der Mutationsbau hatte denselben Ausgabeordner; Original wieder bauen.
    & dotnet run -c Release --project $projekt
    if ($LASTEXITCODE -ne 0) { throw 'Originalpruefung nach Mutation rot.' }
}
