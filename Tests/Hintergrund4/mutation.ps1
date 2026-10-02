$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projekt = Join-Path $PSScriptRoot 'Hintergrund4.csproj'
$ablage = Join-Path $PSScriptRoot 'artifacts'
$kopien = Join-Path $ablage 'mutationen'
New-Item -ItemType Directory -Force $kopien | Out-Null
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/HintergrundAuftragsregel.cs'))
$kurs = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/HintergrundKurspruefung.cs'))
$faelle = @(
    @{Name='vier-versuche'; Art='Auftragsquelle'; Text=$quelle.Replace('>= 3','>= 4').Replace('< 3','< 4')},
    @{Name='ungepruefter-rueckweg'; Art='Auftragsquelle'; Text=$quelle.Replace('&& rueckwegGeprueft','')},
    @{Name='falsche-originalknoten'; Art='Auftragsquelle'; Text=$quelle.Replace('!eigen || !abgerissen','true')},
    @{Name='fehlende-kurse'; Art='Auftragsquelle'; Text=$quelle.Replace('eigenerBesitzer && kante','false && kante')},
    @{Name='vorzeitiger-fortschritt'; Art='Auftragsquelle'; Text=$quelle.Replace('vorgemerkt + hintergrund','vorgemerkt')},
    @{Name='hoehe-ignoriert'; Art='Kursquelle'; Text=$kurs.Replace('math.distance(soll, p)','math.distance(soll.xz, p.xz)')},
    @{Name='falscher-besitzer'; Art='Auftragsquelle'; Text=$quelle.Replace('istAnker(owner) &&','true &&')},
    @{Name='nullhoehe-ungeschuetzt'; Art='Kursquelle'; Text=$kurs.Replace('math.all(elevation == float2.zero)','false')},
    @{Name='kontrollpunkt-ignoriert'; Art='Kursquelle'; Text=$kurs.Replace('&& math.distance(ist.B,soll.B) <= .05f','')},
    @{Name='falsches-tempo'; Art='Kursquelle'; Text=$kurs.Replace('25f / 3.6f','30f / 3.6f')}
)
try {
foreach ($fall in $faelle) {
    $pfad = Join-Path $kopien ($fall.Name + '.cs')
    [IO.File]::WriteAllText($pfad, $fall.Text)
    $arg = '-p:' + $fall.Art + '=' + $pfad
    $ausgabe = & dotnet run -c Release --project $projekt $arg 2>&1
    $code = $LASTEXITCODE
    $ausgabe | Set-Content -LiteralPath (Join-Path $ablage ($fall.Name + '.log')) -Encoding utf8
    if ($code -ne 1 -or -not ($ausgabe -match 'HINTERGRUND4:')) { throw ('Keine echte rote Pruefung: ' + $fall.Name + ', Exit ' + $code) }
    Write-Output ($fall.Name + ': Exit 1, ' + ($ausgabe | Select-Object -Last 1))
}
}
finally {
$gruen = & dotnet run -c Release --project $projekt 2>&1
$code = $LASTEXITCODE
$gruen | Set-Content -LiteralPath (Join-Path $ablage 'hintergrund4.log') -Encoding utf8
$gruen | Write-Output
if ($code -ne 0) { throw 'Produktionsstand besteht nicht.' }

}
