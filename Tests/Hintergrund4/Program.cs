using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

int fehler = 0, n = 0;
void Pruefe(bool gut, string text)
{ n++; if (!gut) { fehler++; Console.WriteLine("FEHLER: " + text); } }
var regel = new HintergrundAuftragsregel<int>();
Pruefe(regel.Einreihen(1),"Gueltiger Auftrag wird gefunden");
Pruefe(!regel.Einreihen(1),"Doppeltes Einreihen verhindert");
Pruefe(regel.Gesperrt(1) && !regel.Gesperrt(2) && regel.Offen == 1,"Sperre nur fuer offenes Lot");
for (int versuch = 1; versuch <= 3; versuch++)
{
    Pruefe(regel.Start(1) == versuch,"Genau ein Start je Versuch");
    Pruefe(!regel.Wiederholen(1,false),"Ohne gemessenen Rueckweg kein Wiederholen");
    Pruefe(regel.Wiederholen(1,true) == (versuch < 3),"Wiederholen hoechstens dreimal");
    regel.Ende(1);
    Pruefe(!regel.Gesperrt(1) && regel.Offen == 0,"Abschluss loest die abgeleitete Sperre");
    Pruefe(regel.Einreihen(1) == (versuch < 3),"Vierter Neubau wird abgewiesen");
}
Pruefe(regel.EinreihenRueckweg(1),"Edit-Rueckweg auch nach drei Neubaufehlern moeglich");
Pruefe(regel.StartRueckweg(1) == 3,"Rueckweg verbraucht keinen Neubauversuch");
regel.Ende(1);
Pruefe(regel.Einreihen(2) && regel.Start(2) == 1,"Andere Lots behalten eigene Versuchszahl");
Pruefe(new HintergrundAuftragsregel<int>().Versuche(1) == 0,"Neue Ladesitzung beginnt mit null Versuchen");
bool ungueltig = false;
try { new HintergrundAuftragsregel<int>().Start(1); } catch (InvalidOperationException) { ungueltig = true; }
Pruefe(ungueltig,"Start ohne Auftrag wird abgewiesen");

int lookups = 0;
int Parent(int _) { lookups++; return 42; }
Pruefe(LotBesitzregel.GehoertZu(42,42,_ => false,Parent) && lookups == 0,"Direkter Besitzer ohne weiteren Lookup");
Pruefe(LotBesitzregel.GehoertZu(10,42,a => a == 10,_ => 42),"Nackter eigener Zoninganker wird gefunden");
Pruefe(!LotBesitzregel.GehoertZu(10,42,_ => true,_ => 43),"Zoninganker eines anderen Lots ausgeschlossen");
Pruefe(!LotBesitzregel.GehoertZu(11,42,_ => false,Parent) && lookups == 0,"Unmarkierter fremder Besitzer ausgeschlossen");

foreach (bool eigen in new[] {false,true})
foreach (bool abgerissen in new[] {false,true})
    Pruefe(RueckwegKnotenregel.OriginalVerwenden(eigen,abgerissen) == !(eigen && abgerissen),
        "Unabhaengige Wahrheitstabelle fuer echte Endknoten");
for (int mask = 0; mask < 32; mask++)
{
    bool besitzer = (mask & 1) != 0, kante = (mask & 2) != 0, geloescht = (mask & 4) != 0;
    bool temp = (mask & 8) != 0, erhalten = (mask & 16) != 0;
    Pruefe(RueckwegKnotenregel.KursSichern(besitzer,kante,geloescht,temp,erhalten) == (mask == 3),
        "Alle 32 Kombinationen der Schnappschussauswahl");
}
// 514 unabhaengig vorgegebene Kanten: jede muss im Plan landen; nur
// die zwei fremden Anschlussknoten behalten die Originalidentitaet.
int kurse = 0, originale = 0;
for (int i = 0; i < 514; i++)
{
    if (RueckwegKnotenregel.KursSichern(true,true,false,false,false)) kurse++;
    if (RueckwegKnotenregel.OriginalVerwenden(i != 0,true)) originale++;
    if (RueckwegKnotenregel.OriginalVerwenden(i != 513,true)) originale++;
}
Pruefe(kurse == 514 && originale == 2,"514 Plan-Kanten und zwei unveraenderte Stadtanschluesse");
foreach (var f in new[] {(10,10,0,0,10),(10,0,10,0,10),(10,0,4,6,10),(10,0,0,10,10),(0,0,1,0,1),(10,3,2,5,10)})
    Pruefe(HintergrundFortschritt.Zaehle(f.Item1,f.Item2,f.Item3) == (f.Item4,f.Item5),
        "Fortschritt aus den zwei tatsaechlich offenen Arbeitsmengen, ohne Doppelzaehlung");
Pruefe(HintergrundKurspruefung.FahrtempoStimmt(25f/3.6f),"25 km/h muessen akzeptiert werden");
Pruefe(!HintergrundKurspruefung.FahrtempoStimmt(30f/3.6f),"30 km/h duerfen nicht als Erfolg gelten");
Pruefe(!HintergrundKurspruefung.FahrtempoStimmt(float.NaN),"Ungueltiger Fahrwert darf nicht bestehen");
Pruefe(HintergrundKurspruefung.NullhoeheFixieren(float2.zero),"Erdgleiche Definition bekommt feste Nullhoehe");
Pruefe(!HintergrundKurspruefung.NullhoeheFixieren(new float2(-10)),"Unterirdische Leitung behaelt echte Elevation");
Pruefe(!HintergrundKurspruefung.NullhoeheFixieren(new float2(0,3)),"Uebergang behält echte Elevation");
for (int i = 0; i < 17; i++)
{
    var soll = new float3(i*2,190+i*.1f,i);
    Pruefe(HintergrundKurspruefung.LageGedeckt(soll,new[] {soll}),"Jede der 17 richtigen Hoehenproben MUSS bestehen");
    Pruefe(!HintergrundKurspruefung.LageGedeckt(soll,new[] {soll-new float3(0,183,0)}),"Identisches XZ mit 183 m Hoehenfehler wird erkannt");
    Pruefe(!HintergrundKurspruefung.LageGedeckt(soll,Array.Empty<float3>()),"Fehlende Kurve ist kein Erfolg");
}
Pruefe(HintergrundKurspruefung.LageGedeckt(float3.zero,new[] {new float3(0,.049f,0)}),"4,9 cm akzeptiert");
Pruefe(!HintergrundKurspruefung.LageGedeckt(float3.zero,new[] {new float3(0,.051f,0)}),"5,1 cm verworfen");
Pruefe(HintergrundKurspruefung.LageGedeckt(float3.zero,new[] {new float3(0,183,0),float3.zero}),"Richtige Teilkurve neben falschem Kandidaten gefunden");
var basis = (new float3(0,190,0),new float3(10,191,5),new float3(20,192,7),new float3(30,193,9));
Pruefe(HintergrundKurspruefung.KurveGleich(basis,basis),"Gesicherte Vierpunktkurve MUSS gefunden werden");
var nahe = (basis.Item1,basis.Item2+new float3(0,.049f,0),basis.Item3,basis.Item4);
Pruefe(HintergrundKurspruefung.KurveGleich(nahe,basis),"4,9 cm am mittleren Kontrollpunkt akzeptiert");
var fern = (basis.Item1,basis.Item2+new float3(0,.051f,0),basis.Item3,basis.Item4);
Pruefe(!HintergrundKurspruefung.KurveGleich(fern,basis),"5,1 cm am mittleren Kontrollpunkt verworfen");
var tief = (basis.Item1,basis.Item2,basis.Item3-new float3(0,183,0),basis.Item4);
Pruefe(!HintergrundKurspruefung.KurveGleich(tief,basis),"Gleiche Enden verdecken keinen mittleren Hoehenfehler");
var umgekehrt = (basis.Item4,basis.Item3,basis.Item2,basis.Item1);
Pruefe(HintergrundKurspruefung.KurveGleich(umgekehrt,basis,true),"Vanilla darf eine exakt umgekehrte Kurve liefern");
Pruefe(!HintergrundKurspruefung.KurveGleich(umgekehrt,basis),"Umgekehrte Kurve ist keine gleich gerichtete Kurve");
var nan = (basis.Item1,new float3(float.NaN),basis.Item3,basis.Item4);
Pruefe(!HintergrundKurspruefung.KurveGleich(nan,basis),"Ungueltiger Kontrollpunkt verworfen");
if (Environment.GetEnvironmentVariable("PLT_NUR_REGELN") != "1") VanillaBefund.Messen(Pruefe);
Console.WriteLine($"HINTERGRUND4: {n} Pruefungen, {fehler} Fehler (Produktionsregeln, keine ECS-Simulation).");
return fehler == 0 ? 0 : 1;
