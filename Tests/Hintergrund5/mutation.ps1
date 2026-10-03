$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ablage = Join-Path $repo '.codex-build/hintergrund5/mutationen'
New-Item -ItemType Directory -Path $ablage -Force | Out-Null
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/HintergrundAbgleich.cs'))
$faelle = @(
    @{Name='knotenhoehe-verloren'; Alt='vorhanden ? knoten : geplant'; Neu='vorhanden && math.distance(geplant.xz,knoten.xz) > .0001f ? knoten : geplant'},
    @{Name='bodensoll-ignoriert'; Alt='bodengebunden && math.distance(ist, boden) <= .05f'; Neu='false'},
    @{Name='objekthoehe-ignoriert'; Alt='math.distance(ist, geplant)'; Neu='math.distance(ist.xz, geplant.xz)'},
    @{Name='schnitt-ignoriert'; Alt='return (a,a+Tangente(von)*(bis-von)/3f,d-Tangente(bis)*(bis-von)/3f,d);'; Neu='return c;'},
    @{Name='knotenkette-ignoriert'; Alt='|| ende != -1 && ende != p.Start'; Neu=''},
    @{Name='halbkurs-gruen'; Alt='return math.abs(erreicht-1f) <= 1e-5f;'; Neu='return true;'},
    @{Name='endlose-frist'; Alt='abgeschlossenePruefungen < MaxPruefungen'; Neu='true'},
    @{Name='seitenparameter-ungekuerzt'; Alt='t = (lage-bereich.x)/(bereich.y-bereich.x);'; Neu='t = lage;'},
    @{Name='zoningknoten-ignoriert'; Alt='gefunden = k.Id;'; Neu='gefunden = -1;'},
    @{Name='fremde-definition-geloescht'; Alt='(angemeldeterBesitzer || eigenerHilfskurs) && permanent'; Neu='permanent'}
)
foreach ($fall in $faelle) {
    if (-not $quelle.Contains($fall.Alt)) { throw ('Mutationsziel fehlt: ' + $fall.Name) }
    $pfad = Join-Path $ablage ($fall.Name + '.cs')
    [IO.File]::WriteAllText($pfad,$quelle.Replace($fall.Alt,$fall.Neu))
    $ausgabe = & dotnet run -c Release --project (Join-Path $PSScriptRoot 'Hintergrund5.csproj') ('-p:Abgleichquelle=' + $pfad) 2>&1
    $code = $LASTEXITCODE
    $ausgabe | Set-Content -LiteralPath (Join-Path $ablage ($fall.Name + '.log')) -Encoding utf8
    if ($code -ne 1 -or -not ($ausgabe -match 'HINTERGRUND5:')) { throw ('Keine echte rote Pruefung: ' + $fall.Name + ', Exit ' + $code) }
    Write-Output ($fall.Name + ': ' + ($ausgabe | Select-Object -Last 1))
}
$ausgabe = & dotnet run -c Release --project (Join-Path $PSScriptRoot 'Hintergrund5.csproj') 2>&1
$code = $LASTEXITCODE
$ausgabe | Set-Content -LiteralPath (Join-Path $ablage 'produktionsstand.log') -Encoding utf8
$ausgabe | Write-Output
if ($code -ne 0) { throw 'Produktionsstand rot.' }
