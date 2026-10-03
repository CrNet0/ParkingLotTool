$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ablage = Join-Path $repo '.codex-build/hintergrund8/mutationen7'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Content -LiteralPath (Join-Path $ablage 'ergebnisse.log') -Value ''
$laeufe = @(
    @('portion-unbegrenzt','Geometry/HintergrundPortion.cs','PortionQuelle','MaxEinheiten = 8','MaxEinheiten = 100000'),
    @('zeitbudget-ignoriert','Geometry/HintergrundPortion.cs','PortionQuelle','BudgetMs = 2','BudgetMs = 100000'),
    @('seitenmerkmal-ignoriert','Geometry/HintergrundSpurregel.cs','SpurQuelle','=> seitenverbindung &&','=> true &&'),
    @('fremdes-objekt-ignoriert','Geometry/HintergrundSpurregel.cs','SpurQuelle','fremdesObjekt(endeId)','true'),
    @('ownerkette-ignoriert','Geometry/HintergrundSpurregel.cs','SpurQuelle','T ziel = owner(objekt);','T ziel = attached(objekt);'),
    @('owneraufstieg-entfernt','Geometry/HintergrundSpurregel.cs','SpurQuelle','T naechster = owner(ziel);','T naechster = leer;'),
    @('publiconly-ignoriert','Geometry/HintergrundSpurregel.cs','SpurQuelle','auto && !nurOeffentlich && !verboten','auto && !verboten'),
    @('gegenrichtung-ignoriert','Geometry/HintergrundSpurregel.cs','SpurQuelle','hin && zurueck &&','hin &&'),
    @('einfahrt-fehlt','Geometry/HintergrundSpurregel.cs','SpurQuelle','(!reinNoetig || rein > 0)','true'),
    @('knotenhoehe-ignoriert','Geometry/HintergrundAbgleich.cs','AbgleichQuelle','math.distance(k.Lage,lage)','math.distance(k.Lage.xz,lage.xz)')
)
Set-Location -LiteralPath $repo
$baupfad = Join-Path $ablage 'bin/'
$objpfad = Join-Path $ablage 'obj/'
$uebersehen = 0
foreach ($lauf in $laeufe) {
    $inhalt = Get-Content -LiteralPath (Join-Path $repo $lauf[1]) -Raw
    if (!$inhalt.Contains($lauf[3])) { throw "Mutationsstelle fehlt: $($lauf[0])" }
    $pfad = Join-Path $ablage ($lauf[0]+'.cs')
    Set-Content -LiteralPath $pfad -Value $inhalt.Replace($lauf[3],$lauf[4])
    $protokoll = Join-Path $ablage ($lauf[0]+'.log')
    dotnet run -c Release --project Tests/Hintergrund7/Hintergrund7.csproj "-p:$($lauf[2])=$pfad" "-p:OutputPath=$baupfad" "-p:IntermediateOutputPath=$objpfad" *> $protokoll
    $code = $LASTEXITCODE
    $ausgabe = Get-Content -LiteralPath $protokoll -Raw
    if ($code -ne 1 -or !$ausgabe.Contains('HINTERGRUND7:') -or !$ausgabe.Contains('FEHLER:')) { $uebersehen++ }
    "$($lauf[0]): Exit=$code, Fehler=$(([regex]::Matches($ausgabe,'FEHLER:')).Count)" |
        Tee-Object -FilePath (Join-Path $ablage 'ergebnisse.log') -Append
}
dotnet run -c Release --project Tests/Hintergrund7/Hintergrund7.csproj "-p:OutputPath=$baupfad" "-p:IntermediateOutputPath=$objpfad" *> (Join-Path $ablage 'produktionsstand.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktionsstand nach Mutationen rot.' }
if ($uebersehen -gt 0) { throw "$uebersehen Mutation(en) ohne gueltigen roten Fachlauf." }
"$($laeufe.Count) Mutationen erkannt; Produktionsdateien unveraendert."
