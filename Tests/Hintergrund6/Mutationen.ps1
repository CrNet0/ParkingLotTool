$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ablage = Join-Path $repo '.codex-build/hintergrund6/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Content -LiteralPath (Join-Path $ablage 'ergebnisse.log') -Value ''
$laeufe = @(
    @('innen-y-wieder-streng','Geometry/HintergrundAbgleich.cs','Abgleichquelle','innenhoeheVanilla ?','false ?'),
    @('end-y-ignoriert','Geometry/HintergrundAbgleich.cs','Abgleichquelle','math.distance(a.A,b.A)','math.distance(a.A.xz,b.A.xz)'),
    @('kontroll-xz-ignoriert','Geometry/HintergrundAbgleich.cs','Abgleichquelle','math.distance(a.B.xz,b.B.xz)','0f'),
    @('proben-wieder-streng','Geometry/HintergrundKurspruefung.cs','Kursquelle','innenhoeheVanilla ?','false ?'),
    @('vanilla-regel-pauschal','Geometry/HintergrundKurspruefung.cs','Kursquelle','=> !geradeKanten || bodengleich && !beideParentMesh && (!flattenTerrain || besitzer);','=> true;'),
    @('pause-ignoriert','Geometry/HintergrundTakt.cs','Taktquelle','=> bild >= fruehestens;','=> true;'),
    @('versuchspause-entfernt','Geometry/HintergrundTakt.cs','Taktquelle','Versuchspause = 120','Versuchspause = 0'),
    @('index-nachbar-fehlt','Geometry/HintergrundLageindex.cs','Indexquelle','int z = -1; z <= 1','int z = 0; z <= 0'),
    @('optionale-liste-null','Tools/ParkingLotBauzettelSchreiber.cs','Schreiberquelle','q.Ausrichtungen = HintergrundTakt.Liste(q.Ausrichtungen);','q.Ausrichtungen = q.Ausrichtungen;'),
    @('puffer-nach-struktur-veraltet','Tools/ParkingLotBauzettelSchreiber.cs','Schreiberquelle','seitenPuffer = EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(lot);','// Veraltete Sicht wieder eingebaut.'),
    @('fremde-definition-beendet','Geometry/HintergrundTakt.cs','Taktquelle','=> eigen && !permanent && !entwurf;','=> !permanent && !entwurf;'),
    @('temp-seed-ignoriert','Geometry/HintergrundDefinitionsherkunft.cs','Herkunftquelle','&& unchecked((uint)(teilseed-quellseed)) < (uint)kursanzahl','&& true'),
    @('temp-lage-ignoriert','Geometry/HintergrundDefinitionsherkunft.cs','Herkunftquelle','&& kontrollXZAbstand <= .05f','&& true'),
    @('temp-hilfsquelle-ignoriert','Geometry/HintergrundDefinitionsherkunft.cs','Herkunftquelle','&& hauptkursBelegt && deklariert','&& true')
)
Set-Location -LiteralPath $repo
$uebersehen = 0
foreach ($lauf in $laeufe) {
    $inhalt = Get-Content -LiteralPath (Join-Path $repo $lauf[1]) -Raw
    if (!$inhalt.Contains($lauf[3])) { throw "Mutationsstelle fehlt: $($lauf[0])" }
    $pfad = Join-Path $ablage ($lauf[0]+'.cs')
    Set-Content -LiteralPath $pfad -Value $inhalt.Replace($lauf[3],$lauf[4])
    $protokoll = Join-Path $ablage ($lauf[0]+'.log')
    dotnet run -c Release --project Tests/Hintergrund6/Hintergrund6.csproj "-p:$($lauf[2])=$pfad" *> $protokoll
    $code = $LASTEXITCODE
    $ausgabe = Get-Content -LiteralPath $protokoll -Raw
    # Kompilierfehler sind kein Mutationsnachweis. Es muss der richtige Test
    # laufen und mindestens eine fachliche Pruefung fehlschlagen.
    if ($code -ne 1 -or !$ausgabe.Contains('HINTERGRUND6:') -or !$ausgabe.Contains('FEHLER:')) { $uebersehen++ }
    "$($lauf[0]): Exit=$code, Fehler=$(([regex]::Matches($ausgabe,'FEHLER:')).Count)" |
        Tee-Object -FilePath (Join-Path $ablage 'ergebnisse.log') -Append
}
dotnet run -c Release --project Tests/Hintergrund6/Hintergrund6.csproj *> (Join-Path $ablage 'produktionsstand.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktionsstand nach Mutationen rot.' }
if ($uebersehen -gt 0) { throw "$uebersehen Mutation(en) ohne gueltigen roten Fachlauf." }
"$($laeufe.Count) Mutationen erkannt; Produktionsdateien unveraendert."
