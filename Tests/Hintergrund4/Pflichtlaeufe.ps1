$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projekt = Join-Path $repo 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
$ablage = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $ablage -Force | Out-Null
$ergebnisse = [Collections.Generic.List[object]]::new()
$namen = @('', 'migrationen', 'statusmeldungen', 'zoningflaeche', 'zoningnetz',
    'zoningrasten', 'ueberlappung', 'versorgung', 'infokarten', 'flaechenannahme',
    'zufahrtsverlust', 'sonderplaetze', 'zufahrtsquads', 'lformtoggle',
    'fusswegzugang', 'gassenabstand', 'endwegbreite', 'werkzeugzustand',
    'diagonalwege', 'winkelmodus', 'randzoningseite', 'querstrassengitter',
    'geradepunkt', 'rzstufen', 'ortsunabhaengig', 'rzanschluss', 'entarteteringe')
foreach ($name in $namen) {
    $argumente = @('run', '-c', 'Release', '--project', $projekt)
    if ($name -ne '') { $argumente += @('--no-build', '--', "--$name") }
    if ($name -eq 'sonderplaetze') {
        $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113',
            '0,51.4;1,37.9;2,51.8;3,74.0')
    }
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    $ausgabe = & dotnet @argumente 2>&1
    $code = $LASTEXITCODE
    $label = if ($name -eq '') { 'ohne' } else { $name }
    $ausgabe | Set-Content -LiteralPath (Join-Path $ablage "$label.log") -Encoding utf8
    $ergebnisse.Add([pscustomobject]@{ Lauf = $label; Exit = $code; Sekunden = $uhr.Elapsed.TotalSeconds })
    $ergebnisse | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ablage 'Pflichtlaeufe.json') -Encoding utf8
    Write-Output "$label : Exit $code, $([math]::Round($uhr.Elapsed.TotalSeconds, 2)) s"
}
if (@($ergebnisse | Where-Object { $_.Exit -ne 0 }).Count -ne 0) { exit 1 }
