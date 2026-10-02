$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ausgabe = Join-Path $PSScriptRoot 'artifacts/pflicht'
New-Item -ItemType Directory -Force -Path $ausgabe | Out-Null
Set-Location -LiteralPath $repo
$projekt = 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
dotnet build -c Release $projekt *> (Join-Path $ausgabe 'geometry-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Geometrietest kompiliert nicht.' }
$laeufe = @('standard', 'zoningflaeche', 'zoningnetz', 'zoningrasten', 'ueberlappung',
    'versorgung', 'infokarten', 'flaechenannahme', 'zufahrtsverlust', 'sonderplaetze',
    'zufahrtsquads', 'lformtoggle', 'fusswegzugang', 'gassenabstand', 'endwegbreite',
    'werkzeugzustand', 'diagonalwege', 'winkelmodus', 'randzoningseite', 'querstrassengitter',
    'geradepunkt', 'rzstufen', 'ortsunabhaengig', 'rzanschluss', 'entarteteringe',
    'migrationen', 'statusmeldungen')
$fehler = 0
foreach ($lauf in $laeufe) {
    $argumente = @()
    if ($lauf -ne 'standard') { $argumente = @('--', "--$lauf") }
    if ($lauf -eq 'sonderplaetze') {
        $argumente += '-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113'
        $argumente += '0,51.4;1,37.9;2,51.8;3,74.0'
    }
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    dotnet run -c Release --no-build --project $projekt @argumente *> (Join-Path $ausgabe "$lauf.log")
    $code = $LASTEXITCODE
    if ($code -ne 0) { $fehler++ }
    "$lauf Exit=$code Zeit=$([math]::Round($uhr.Elapsed.TotalSeconds,1))s" | Tee-Object -FilePath (Join-Path $ausgabe 'ergebnisse.log') -Append
}
foreach ($lauf in @('Versorgungsneubau', 'DiagonaleEndwege', 'Hintergrund4', 'Fahrprefabs', 'Versorgungsabsturz')) {
    dotnet run -c Release --project "Tests/$lauf/$lauf.csproj" *> (Join-Path $ausgabe "$lauf.log")
    $code = $LASTEXITCODE
    if ($code -ne 0) { $fehler++ }
    "$lauf Exit=$code" | Tee-Object -FilePath (Join-Path $ausgabe 'ergebnisse.log') -Append
}
if ($fehler -gt 0) { throw "$fehler Pflichtlauf/-laeufe fehlgeschlagen. Ausgaben: $ausgabe" }
"32 Laeufe erfolgreich. Ausgaben: $ausgabe"
