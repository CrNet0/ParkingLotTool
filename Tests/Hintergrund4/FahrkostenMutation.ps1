$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projekt = Join-Path $repo 'Tests/Fahrprefabs/Fahrprefabs.csproj'
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotFahrwegeMigration.cs'))
$zeile = 'return Unity.Mathematics.math.any(Unity.Mathematics.math.abs(ist - soll) > 0.00001f);'
if (($quelle.Split(@($zeile), [StringSplitOptions]::None).Length - 1) -ne 1) {
    throw 'Kostenpruefung geaendert: Mutation aktualisieren.'
}
$ablage = Join-Path $PSScriptRoot 'artifacts'
$mutiert = Join-Path $ablage 'fahrkosten-mutiert.txt'
try {
    [IO.File]::WriteAllText($mutiert, $quelle.Replace($zeile, 'return false;'))
    $ausgabe = & dotnet run -c Release --project $projekt "-p:FahrmigrationQuelle=$mutiert" 2>&1
    $code = $LASTEXITCODE
    $ausgabe | Set-Content -LiteralPath (Join-Path $ablage 'fahrkosten-mutation.log') -Encoding utf8
    if ($code -ne 1 -or -not ($ausgabe -match 'FAHRPREFABS: 65 Pruefungen, 2 Fehler')) {
        throw 'Kostenmutation muss beide echten Kostenpruefungen rot machen.'
    }
    Write-Output 'Kostenmutation: Exit 1, 2 Fehler (erwartet).'
}
finally {
    Remove-Item -LiteralPath $mutiert -ErrorAction SilentlyContinue
    $gruen = & dotnet run -c Release --project $projekt 2>&1
    $code = $LASTEXITCODE
    $gruen | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'fahrprefabs.log') -Encoding utf8
    $gruen | Write-Output
    if ($code -ne 0) { throw 'Produktionspruefung nach Kostenmutation rot.' }
}
