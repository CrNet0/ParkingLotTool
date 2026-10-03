$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ablage = Join-Path $repo '.codex-build/hintergrund8/mutationen8'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Content -LiteralPath (Join-Path $ablage 'ergebnisse.log') -Value ''
$laeufe = @(
    @('erhalt-ausgeschaltet','=> prefabGleich && richtungGleich','=> false && prefabGleich && richtungGleich'),
    @('prefabwechsel-erhalten','=> prefabGleich && richtungGleich','=> richtungGleich'),
    @('richtungswechsel-erhalten','prefabGleich && richtungGleich &&','prefabGleich &&'),
    @('stadt-id-ignoriert','richtungGleich && anschlussGleich &&','richtungGleich &&'),
    @('gegenpuffer-ignoriert','anschlussGleich && beidseitigVerbunden','anschlussGleich'),
    @('zoningseite-ignoriert','&& seitenGleich && math.isfinite','&& math.isfinite'),
    @('kursgrenze-aufgeweicht','kursabstand <= .05f','kursabstand <= 1f'),
    @('luecke-erhalten','math.abs(erreicht-1f) > 1e-5f','false'),
    @('zwischen-id-ignoriert','!EqualityComparer<T>.Default.Equals(letzter,p.Start)','false'),
    @('kontrollpunkt-ignoriert','math.distance(p.Kurve.B.xz,s.B.xz)','0f'),
    @('erhalten-fehlt-trotzdem-gruen','erhalten + neueSoll == geplant','true'),
    @('neubau-fehlt-trotzdem-gruen','neu == neueSoll','true')
)
$inhalt = Get-Content -LiteralPath (Join-Path $repo 'Geometry/Netzerhalt.cs') -Raw
$uebersehen = 0
Set-Location -LiteralPath $repo
$baupfad = Join-Path $ablage 'bin/'
$objpfad = Join-Path $ablage 'obj/'
foreach ($lauf in $laeufe) {
    if (!$inhalt.Contains($lauf[1])) { throw "Mutationsstelle fehlt: $($lauf[0])" }
    $quelle = Join-Path $ablage ($lauf[0]+'.cs')
    Set-Content -LiteralPath $quelle -Value $inhalt.Replace($lauf[1],$lauf[2])
    $log = Join-Path $ablage ($lauf[0]+'.log')
    dotnet run -c Release --project Tests/Hintergrund8/Hintergrund8.csproj "-p:ErhaltQuelle=$quelle" "-p:OutputPath=$baupfad" "-p:IntermediateOutputPath=$objpfad" *> $log
    $code = $LASTEXITCODE
    $ausgabe = Get-Content -LiteralPath $log -Raw
    if ($code -ne 1 -or !$ausgabe.Contains('HINTERGRUND8:') -or !$ausgabe.Contains('FEHLER:')) { $uebersehen++ }
    "$($lauf[0]): Exit=$code, Fehler=$(([regex]::Matches($ausgabe,'FEHLER:')).Count)" |
        Tee-Object -FilePath (Join-Path $ablage 'ergebnisse.log') -Append
}
dotnet run -c Release --project Tests/Hintergrund8/Hintergrund8.csproj "-p:OutputPath=$baupfad" "-p:IntermediateOutputPath=$objpfad" *> (Join-Path $ablage 'produktionsstand.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktionsstand nach Mutationen rot.' }
if ($uebersehen -gt 0) { throw "$uebersehen Mutationen ohne roten Fachlauf." }
"$($laeufe.Count) Mutationen erkannt; Produktionsdateien unveraendert."
