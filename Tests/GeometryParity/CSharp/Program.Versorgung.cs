using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int PruefeVersorgungskurse()
    {
        var fehler = 0;
        var pruefungen = 0;
        void Pruefe(bool ok, string name)
        {
            pruefungen++;
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER Versorgung: " + name);
        }
        Pruefe(!VersorgungskursPruefung.Vollstaendig(8, new List<float2>()), "Nichts-Tun");
        Pruefe(!VersorgungskursPruefung.Vollstaendig(8, new List<float2> { new float2(0, 3) }), "nur Teilkante");
        Pruefe(!VersorgungskursPruefung.Vollstaendig(8, new List<float2> { new float2(0, 3), new float2(4, 8) }), "Luecke");
        Pruefe(VersorgungskursPruefung.Vollstaendig(8, new List<float2> { new float2(4, 8), new float2(0, 4) }), "geteilte gueltige Leitung");
        Pruefe(VersorgungskursPruefung.Vollstaendig(13, new List<float2> { new float2(0, 13) }), "gueltige ganze Leitung");
        Pruefe(VersorgungskursPruefung.Vollstaendig(8, new List<float2> { new float2(0, 7.99999f) }), "Epsilon");
        var start = new float2(0, 0);
        var ende = new float2(8, 0);
        Pruefe(VersorgungskursPruefung.Abschnitt(start, ende, ende, new float2(5, 0), new float2(3, 0), start, out _), "umgekehrte Richtung");
        Pruefe(!VersorgungskursPruefung.Abschnitt(start, ende, start + 3, new float2(3, 3), new float2(5, 3), ende + 3, out _), "fremde parallele Leitung");
        Pruefe(!VersorgungskursPruefung.Abschnitt(start, ende, start, new float2(3, 3), new float2(5, 3), ende, out _), "ausgebogene Kurve");
        Pruefe(VersorgungskursPruefung.Achsabstand(1, 1) == 3f, "bisherige 3 m bei schmalen Prefabs");
        Pruefe(VersorgungskursPruefung.Achsabstand(4, 4) >= 4.25f, "breite Prefabs ohne Ueberlappung");
        float3[] Gerade(float3 a, float3 b) => new[] { a, math.lerp(a, b, 1f/3), math.lerp(a, b, 2f/3), b };
        var linie = Gerade(new float3(0, -10, 0), new float3(8, -10, 0));
        var hoehe = new float2(-0.5f, 0.5f);
        Pruefe(Versorgungsabstand.Beruehrt(linie, 1, hoehe,
            Gerade(new float3(4, -10, -5), new float3(4, -10, 5)), 1, hoehe), "kreuzende Fremdleitung");
        Pruefe(Versorgungsabstand.Beruehrt(linie, 1, hoehe, linie, 1, hoehe), "deckungsgleiche Leitung");
        Pruefe(!Versorgungsabstand.Beruehrt(linie, 1, hoehe,
            Gerade(new float3(0, -10, 3), new float3(8, -10, 3)), 1, hoehe), "gueltiger Parallelabstand");
        Pruefe(!Versorgungsabstand.Beruehrt(linie, 1, hoehe,
            Gerade(new float3(4, -15, -5), new float3(4, -15, 5)), 1, hoehe), "Kreuzung in anderer Tiefe");
        Pruefe(VersorgungskursPruefung.Zusammenhaengend(new List<int2> {
            new int2(0, 1), new int2(2, 1), new int2(2, 3) }, 0, 3), "3 Teilstrecken mit gemeinsamen Entities");
        Pruefe(!VersorgungskursPruefung.Zusammenhaengend(new List<int2> {
            new int2(0, 1), new int2(2, 3) }, 0, 3), "deckungsgleicher Knick mit 2 verschiedenen Entities bleibt getrennt");
        Pruefe(!VersorgungskursPruefung.Zusammenhaengend(new List<int2> {
            new int2(0, 1), new int2(2, 3) }, 0, 1), "abgetrenntes Reststueck");
        Versorgungsweg.Hindernis Rechteck(float x0, float y0, float x1, float y1, int id = 1)
            => new Versorgungsweg.Hindernis { Strasse = id, Ring = new[] {
                new float2(x0,y0), new float2(x1,y0), new float2(x1,y1), new float2(x0,y1) } };
        var sperren = new List<Versorgungsweg.Hindernis> { Rechteck(4, -3, 6, 3) };
        Versorgungsweg.Ergebnis Suche(List<Versorgungsweg.Hindernis> h, float2 a, float2 b)
            => Versorgungsweg.Suche(new List<float2> { a }, h, _ => new HashSet<int>(),
                _ => new[] { new Versorgungsweg.Ziel { Punkt = b, Index = 0 } }, null);
        var umweg = Suche(sperren, start, new float2(10, 0));
        Pruefe(umweg.Punkte != null && umweg.Punkte.Count == 4 && math.abs(umweg.Laenge - 12f) < 0.01f,
            "Rechteck: kuerzester Umweg 12 m mit 3 Teilstrecken statt blockierter 10-m-Gerade");
        var abbruchGerade = Versorgungsnetz.Gerade(new List<float2> { start },
            _ => new[] { new Versorgungsweg.Ziel { Punkt = ende } }, (_, __) => true,
            abgebrochen: () => true);
        Pruefe(abbruchGerade.Punkte == null, "abgebrochene Geradensuche liefert keine Trasse");
        var abbruchUmweg = Versorgungsweg.Suche(new List<float2> { start }, sperren,
            _ => new HashSet<int>(), _ => new[] { new Versorgungsweg.Ziel { Punkt = ende } },
            null, abgebrochen: () => true);
        Pruefe(abbruchUmweg.Punkte == null, "abgebrochene Graphsuche liefert keine Trasse");
        Pruefe(!Versorgungsweg.Frei(start, new float2(10, 0), sperren), "durchgehende Hindernispruefung");
        Pruefe(Versorgungsweg.Frei(new float2(0, 3), new float2(10, 3), sperren), "Tangente an aufgeweiteter Grenze");
        Pruefe(!Versorgungsweg.Frei(start, new float2(10, 0),
            new List<Versorgungsweg.Hindernis> { Rechteck(4.31f, -1, 4.39f, 1) }), "8-cm-Hindernis zwischen alten Meterproben");
        var geschlossen = new List<Versorgungsweg.Hindernis> {
            Rechteck(-10,-10,10,-8), Rechteck(-10,8,10,10), Rechteck(-10,-10,-8,10), Rechteck(8,-10,10,10) };
        var keinWeg = Suche(geschlossen, start, new float2(20, 0));
        Pruefe(keinWeg.Punkte == null && keinWeg.Erreicht > 0 && keinWeg.Zielpruefungen > 0, "geschlossener Ring: 0 Wege mit Suchzahlen");
        geschlossen.RemoveAt(1);
        var offen = Suche(geschlossen, start, new float2(20, 0));
        Pruefe(offen.Punkte != null && offen.Laenge > 20, "U-Form: vorhandener Ausgang wird gefunden");
        if (offen.Punkte != null)
            for (var i = 1; i < offen.Punkte.Count; i++)
                Pruefe(Versorgungsweg.Frei(offen.Punkte[i-1], offen.Punkte[i], geschlossen), "U-Umweg Teilstrecke frei");
        var ausgang = new List<Versorgungsweg.Hindernis> { Rechteck(-2, -2, 2, 2, 1) };
        Pruefe(Versorgungsweg.Frei(start, new float2(10, 0), ausgang, new HashSet<int> { 1 }, 8), "Startstrasse verlassen");
        ausgang.Add(Rechteck(3, -2, 4, 2, 2));
        Pruefe(!Versorgungsweg.Frei(start, new float2(10, 0), ausgang, new HashSet<int> { 1 }, 8), "Nachbarstrasse im 8-m-Bereich bleibt gesperrt");
        Pruefe(!Versorgungsweg.Frei(start, new float2(10, 0), ausgang), "keine neue Startausnahme am Knick");
        var knick = new List<float2> { start, new float2(10,0), new float2(10,10) };
        var links = Versorgungsweg.Versetze(knick, -1.5f);
        var rechts = Versorgungsweg.Versetze(knick, 1.5f);
        Pruefe(links != null && rechts != null && math.distance(links[1], new float2(11.5f,-1.5f)) < 0.01f
            && math.distance(rechts[1], new float2(8.5f,1.5f)) < 0.01f, "90-Grad-Gehrung ohne Luecken");
        Pruefe(Versorgungsweg.Versetze(new List<float2> { start, new float2(10,0), start }, 1.5f) == null, "Kehrtwende erzeugt keine Ueberlappung");
        var bogenhuelle = new List<Versorgungsweg.Hindernis>();
        Versorgungsweg.Bogen(bogenhuelle, 1, new float2(0,0), new float2(0,10), new float2(10,10), new float2(10,0), 1);
        Pruefe(!Versorgungsweg.Frei(new float2(5,6), new float2(5,9), bogenhuelle), "Bezier-Ausbauchung bleibt Hindernis");
        Pruefe(Versorgungsweg.Spuren(knick, 1, 1, new List<Versorgungsweg.Hindernis>(), null, 8, out _, out _),
            "2 vollstaendige versetzte Kurse am 90-Grad-Knick baubar");
        Pruefe(!Versorgungsweg.Spuren(knick, 1, 1,
            new List<Versorgungsweg.Hindernis> { Rechteck(2, 1.2f, 4, 2) }, null, 8, out _, out _),
            "freie Mittellinie reicht nicht: Wasser liegt in Hindernis");
        Pruefe(Versorgungsweg.Versetze(new List<float2> { start, new float2(float.NaN, 2) }, 1) == null,
            "nicht endlicher Kurs abgewiesen");
        for (var winkel = 0; winkel < 360; winkel += 5)
        {
            var w = winkel * math.PI / 180;
            float2 Drehe(float2 p) => new float2(p.x * math.cos(w) - p.y * math.sin(w),
                p.x * math.sin(w) + p.y * math.cos(w)) + new float2(-1052.6f, -32.9f);
            var gedreht = Rechteck(4,-3,6,3);
            for (var i = 0; i < gedreht.Ring.Length; i++) gedreht.Ring[i] = Drehe(gedreht.Ring[i]);
            var probe = Suche(new List<Versorgungsweg.Hindernis> { gedreht }, Drehe(start), Drehe(new float2(10,0)));
            Pruefe(probe.Punkte != null && math.abs(probe.Laenge - 12) < 0.01f, $"12-m-Umweg bei Weltkoordinaten, Drehung {winkel}");
        }
        PruefeVersorgungsFehlermuster(Pruefe);
        PruefeVersorgungsRinge(Pruefe);
        // Nachstellung einer 43,702-m-Geraden zur Stadtstrasse.
        Versorgungskante Kante(int id, float x, bool stadt = false,
            float z0 = 0, float z1 = 10, float y = 0)
        {
            var a = new float3(x, y, z0); var b = new float3(x, y, z1);
            return new Versorgungskante { Id = id, Startknoten = id * 2,
                Endknoten = id * 2 + 1, Startpunkt = a, Endpunkt = b,
                SteuerungB = math.lerp(a, b, 1f / 3).xz,
                SteuerungC = math.lerp(a, b, 2f / 3).xz,
                Position = t => math.lerp(a, b, t),
                Projektion = p => {
                    var q = Versorgungsnetz.Projektion(p, a.xz, b.xz);
                    return new float3(q.x, y, q.y);
                },
                Breite = stadt ? 8 : 4, Versorgung = true,
                Stadt = stadt, Stromtor = true, Wassertor = true,
                Stromfang = 1, Wasserfang = 1 };
        }
        var ePlan = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
            Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
        var erstes = Kante(1, -100); erstes.KnotenAnStadt = true;
        var zweites = Kante(2, 0, false, -20, 40);
        var stadtziel = Kante(3, 43.702f, true, 0, 30);
        ePlan.Eigene.Add(zweites);
        ePlan.Hinderniskanten.Add(zweites);
        ePlan.Ziele.Add(zweites); ePlan.Ziele.Add(stadtziel);
        var plan = VersorgungstrassenPlan.Waehle(ePlan);
        Pruefe(plan.Gruppen.Count == 1 && plan.PerKnotenAnStadt == 0
            && plan.OffeneTeile == 1, "ein offenes Netz vor Stadtanschluss");
        Pruefe(plan.Beste != null && plan.Beste.Zielkante == stadtziel
            && math.abs(plan.Beste.Laenge - 43.702f) < 0.01f
            && plan.Beste.Startkanten.Count > 0,
            "gemeinsamer Kern findet 43,702-m-Trasse vom zweiten Netz");
        if (plan.Beste != null)
        {
            var w = plan.Beste;
            var vor = new Versorgungsvorplan { ZielId = w.Zielkante.Id,
                OffeneTeile = 1,
                Start = w.Start.xz, Ziel = w.Ziel.xz, Laenge = w.Laenge,
                Hindernisweg = w.Hindernisweg, Punkte = new List<float2>(w.Punkte),
                Stromweg = new List<float2>(w.Stromweg),
                Wasserweg = new List<float2>(w.Wasserweg) };
            Pruefe(VersorgungstrassenPlan.PruefeVorplan(ePlan, vor, out var bestaetigt, out _)
                && bestaetigt.Beste != null
                && bestaetigt.Beste.Zielkante == w.Zielkante
                && math.distance(bestaetigt.Beste.Start.xz, w.Start.xz) < 0.001f
                && math.distance(bestaetigt.Beste.Ziel.xz, w.Ziel.xz) < 0.001f
                && math.abs(bestaetigt.Beste.Laenge - w.Laenge) < 0.001f,
                "gueltiger Vorplan liefert dieselbe Trasse wie die Wahl");
            var ohneZiel = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            ohneZiel.Eigene.AddRange(ePlan.Eigene);
            ohneZiel.Hinderniskanten.AddRange(ePlan.Hinderniskanten);
            ohneZiel.Ziele.Add(erstes); ohneZiel.Ziele.Add(zweites);
            Pruefe(!VersorgungstrassenPlan.PruefeVorplan(ohneZiel, vor, out _, out _),
                "geloeschte Zielkante verwirft Vorplan");
            var geteiltesStadtziel = new Versorgungseingabe { Strombreite = 1,
                Wasserbreite = 1, Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            geteiltesStadtziel.Eigene.Add(zweites);
            geteiltesStadtziel.Hinderniskanten.Add(zweites);
            geteiltesStadtziel.Ziele.Add(Kante(300, 43.702f, true,
                vor.Ziel.y - 8, vor.Ziel.y + 8));
            Pruefe(VersorgungstrassenPlan.PruefeVorplan(geteiltesStadtziel,
                vor, out var nachTeilung, out _)
                && nachTeilung.Beste.Zielkante.Id == 300,
                "Stadtziel bleibt nach Kanten-Teilung am Ort erkennbar");
            var fremderStart = new Versorgungsvorplan { ZielId = vor.ZielId,
                OffeneTeile = 1,
                Start = erstes.Position(0.5f).xz, Ziel = vor.Ziel,
                Laenge = vor.Laenge, Punkte = vor.Punkte,
                Stromweg = vor.Stromweg, Wasserweg = vor.Wasserweg };
            Pruefe(!VersorgungstrassenPlan.PruefeVorplan(ePlan, fremderStart, out _, out _),
                "Startpunkt auf fremdem Netz verwirft Vorplan");
            var mitHindernis = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            mitHindernis.Eigene.AddRange(ePlan.Eigene);
            mitHindernis.Hinderniskanten.AddRange(ePlan.Hinderniskanten);
            mitHindernis.Hinderniskanten.Add(Kante(4, 20, false, -5, 25));
            mitHindernis.Ziele.AddRange(ePlan.Ziele);
            Pruefe(!VersorgungstrassenPlan.PruefeVorplan(mitHindernis, vor, out _, out _),
                "neues Hindernis durch Trasse verwirft Vorplan");
            var mitUmweg = VersorgungstrassenPlan.Waehle(mitHindernis).Beste;
            Pruefe(mitUmweg != null && mitUmweg.Hindernisweg,
                "Hindernis erzwingt einen gefundenen Umweg");
            if (mitUmweg != null && mitUmweg.Hindernisweg)
            {
                var vorUmweg = new Versorgungsvorplan { ZielId = mitUmweg.Zielkante.Id,
                    OffeneTeile = 1,
                    Start = mitUmweg.Start.xz, Ziel = mitUmweg.Ziel.xz,
                    Laenge = mitUmweg.Laenge, Hindernisweg = true,
                    Punkte = mitUmweg.Punkte, Stromweg = mitUmweg.Stromweg,
                    Wasserweg = mitUmweg.Wasserweg };
                Pruefe(VersorgungstrassenPlan.PruefeVorplan(mitHindernis,
                    vorUmweg, out var bestaetigterUmweg, out _)
                    && bestaetigterUmweg.Beste != null
                    && bestaetigterUmweg.Beste.Hindernisweg,
                    "gueltiger Hindernisweg wird uebernommen");
            }
            var mitHoehen = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            var gebauterStart = Kante(2, 0, false, -20, 40, 5);
            var gebautesZiel = Kante(3, 43.702f, true, 0, 30, 8);
            mitHoehen.Eigene.Add(gebauterStart);
            mitHoehen.Hinderniskanten.Add(gebauterStart);
            mitHoehen.Ziele.Add(gebautesZiel);
            var hoehenOk = VersorgungstrassenPlan.PruefeVorplan(mitHoehen, vor,
                out var erhoeht, out _);
            Pruefe(hoehenOk
                && math.abs(erhoeht.Beste.Start.y - 5) < 0.001f
                && math.abs(erhoeht.Beste.Ziel.y - 8) < 0.001f,
                "Isthoehen kommen aus den gebauten Kurven");
            var zweiOffene = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            zweiOffene.Eigene.Add(zweites);
            zweiOffene.Eigene.Add(Kante(5, -50));
            zweiOffene.Hinderniskanten.AddRange(zweiOffene.Eigene);
            zweiOffene.Ziele.Add(stadtziel);
            Pruefe(!VersorgungstrassenPlan.PruefeVorplan(zweiOffene, vor, out _, out _),
                "zweites offenes Netz erzwingt volle Wahl");
            var eigenesZiel = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            var quelle = Kante(10, 0);
            var anStadt = Kante(11, 20); anStadt.KnotenAnStadt = true;
            eigenesZiel.Eigene.Add(quelle); eigenesZiel.Eigene.Add(anStadt);
            eigenesZiel.Hinderniskanten.AddRange(eigenesZiel.Eigene);
            eigenesZiel.Ziele.Add(quelle); eigenesZiel.Ziele.Add(anStadt);
            eigenesZiel.Ziele.Add(Kante(12, 100, true));
            var eigenerWeg = VersorgungstrassenPlan.Waehle(eigenesZiel).Beste;
            Pruefe(eigenerWeg != null && eigenerWeg.Zielkante == anStadt,
                "Wahl findet Ziel auf eigenem, stadtverbundenem Netz");
            if (eigenerWeg != null && eigenerWeg.Zielkante == anStadt)
            {
                var vorEigen = new Versorgungsvorplan { ZielId = -11, ZielEigene = true,
                    OffeneTeile = 1,
                    Start = eigenerWeg.Start.xz, Ziel = eigenerWeg.Ziel.xz,
                    Laenge = eigenerWeg.Laenge, Hindernisweg = eigenerWeg.Hindernisweg,
                    Punkte = eigenerWeg.Punkte, Stromweg = eigenerWeg.Stromweg,
                    Wasserweg = eigenerWeg.Wasserweg };
                Pruefe(VersorgungstrassenPlan.PruefeVorplan(eigenesZiel,
                    vorEigen, out var bestaetigtEigen, out _)
                    && bestaetigtEigen.Beste.Zielkante == anStadt,
                    "eigenes Ziel wird nach dem Bau eindeutig zugeordnet");
                var eigenesGeteilt = new Versorgungseingabe { Strombreite = 1,
                    Wasserbreite = 1, Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
                eigenesGeteilt.Eigene.Add(quelle);
                eigenesGeteilt.Hinderniskanten.Add(quelle);
                var zielteil = Kante(111, 20, false,
                    vorEigen.Ziel.y - 8, vorEigen.Ziel.y + 8);
                zielteil.KnotenAnStadt = true;
                eigenesGeteilt.Eigene.Add(zielteil);
                eigenesGeteilt.Hinderniskanten.Add(zielteil);
                eigenesGeteilt.Ziele.Add(quelle);
                eigenesGeteilt.Ziele.Add(zielteil);
                Pruefe(VersorgungstrassenPlan.PruefeVorplan(eigenesGeteilt,
                    vorEigen, out var nachEigenerTeilung, out _)
                    && nachEigenerTeilung.Beste.Zielkante == zielteil,
                    "eigenes Ziel bleibt nach Kanten-Teilung erkennbar");
            }
        }
        Versorgungseingabe DreiNetze(float stadtX = -100)
        {
            var e = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            var amKnoten = Kante(30, stadtX); amKnoten.KnotenAnStadt = true;
            var abseits = Kante(31, 0);
            var zumEigenen = Kante(32, -25);
            e.Eigene.Add(amKnoten); e.Eigene.Add(abseits); e.Eigene.Add(zumEigenen);
            e.Hinderniskanten.AddRange(e.Eigene);
            e.Ziele.AddRange(e.Eigene);
            e.Ziele.Add(Kante(33, 20, true));
            return e;
        }
        var folge = VersorgungstrassenPlan.PlaneFolge(DreiNetze(), out var anfang);
        Pruefe(anfang.PerKnotenAnStadt == 1 && anfang.OffeneTeile == 2
            && folge.Count == 2 && folge[0].ZielEigene && folge[1].ZielEigene,
            "drei getrennte Netze: erst eigene Teile, kein weiterer Stadtanschluss");
        var istFolge = DreiNetze();
        for (var i = 0; i < folge.Count; i++)
        {
            var volleWahl = VersorgungstrassenPlan.Waehle(istFolge).Beste;
            var trasse = folge[i];
            Pruefe(volleWahl != null && volleWahl.Zielkante.Id == trasse.ZielId
                && math.distance(volleWahl.Start.xz, trasse.Start) < 0.01f
                && math.distance(volleWahl.Ziel.xz, trasse.Ziel) < 0.01f
                && math.abs(volleWahl.Laenge - trasse.Laenge) < 0.01f
                && !trasse.AnderesNetzKuerzer,
                $"Vorplan-Folge entspricht voller Wahl bei Verbindung {i + 1}");
            Pruefe(VersorgungstrassenPlan.PruefeVorplan(istFolge, trasse,
                out var bestaetigt, out _) && bestaetigt.Beste.Zielkante.Id == trasse.ZielId,
                $"gebaute Eingabe nimmt Vorplan-Trasse {i + 1} an");
            istFolge.Verbindungen.Add((volleWahl.Start.xz, volleWahl.Ziel.xz,
                volleWahl.Zielkante.Stadt));
        }
        if (folge.Count == 2)
        {
            var mutation = new Versorgungsvorplan { ZielId = folge[0].ZielId,
                OffeneTeile = folge[0].OffeneTeile, Start = folge[0].Start,
                Ziel = folge[0].Ziel, Laenge = folge[0].Laenge + 5,
                Punkte = folge[0].Punkte, Stromweg = folge[0].Stromweg,
                Wasserweg = folge[0].Wasserweg };
            var rueckfall = DreiNetze();
            Pruefe(!VersorgungstrassenPlan.PruefeVorplan(rueckfall, mutation,
                out _, out _), "Mutation der ersten Trasse wird verworfen");
            for (var i = 0; i < folge.Count; i++)
            {
                var voll = VersorgungstrassenPlan.Waehle(rueckfall).Beste;
                Pruefe(voll != null && voll.Zielkante.Id == folge[i].ZielId,
                    $"nach Mutation volle Wahl fuer Anlauf {i + 1}");
                rueckfall.Verbindungen.Add((voll.Start.xz, voll.Ziel.xz,
                    voll.Zielkante.Stadt));
            }
        }
        Versorgungseingabe ZonenOhneStadtpfad(float stadtX = -15)
        {
            var e = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
                Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
            foreach (var (id, x) in new[] { (40, 0f), (41, 30f), (42, 70f) })
            {
                var k = Kante(id, x, false, 0, 30);
                e.Eigene.Add(k); e.Hinderniskanten.Add(k); e.Ziele.Add(k);
            }
            e.Ziele.Add(Kante(43, stadtX, true, 0, 30));
            return e;
        }
        var dreiZonen = VersorgungstrassenPlan.PlaneFolge(ZonenOhneStadtpfad(), out _);
        Pruefe(dreiZonen.Count == 3 && dreiZonen[0].ZielEigene
            && dreiZonen[1].ZielEigene && !dreiZonen[2].ZielEigene
            && math.abs(dreiZonen[2].Start.x) < 0.01f,
            "drei Zonen: zwei eigene Verbindungen, ein Stadtanschluss von Zone 0");
        var stadtLockt = VersorgungstrassenPlan.PlaneFolge(ZonenOhneStadtpfad(-6), out _);
        Pruefe(stadtLockt.Count == 3 && stadtLockt[0].ZielEigene
            && stadtLockt[1].ZielEigene && !stadtLockt[2].ZielEigene,
            "Mutation: naehere Stadtstrasse darf eigene Reihenfolge nicht ueberholen");
        var nurStadtausweg = ZonenOhneStadtpfad(15);
        nurStadtausweg.Eigene.RemoveAt(2);
        nurStadtausweg.Hinderniskanten.RemoveAt(2);
        nurStadtausweg.Ziele.RemoveAt(2);
        nurStadtausweg.GesperrteZiele[40] = new HashSet<int> { 41 };
        nurStadtausweg.GesperrteZiele[41] = new HashSet<int> { 40 };
        var ausnahme = VersorgungstrassenPlan.PlaneFolge(nurStadtausweg, out _);
        Pruefe(ausnahme.Count == 2 && !ausnahme[0].ZielEigene
            && !ausnahme[1].ZielEigene,
            "gesperrte eigene Ziele: zwei isolierte Teile brauchen Stadt-Rueckfall");
        var stadtVorweg = ZonenOhneStadtpfad();
        foreach (var k in stadtVorweg.Eigene)
            stadtVorweg.GesperrteZiele[k.Id] = new HashSet<int>(
                stadtVorweg.Eigene.FindAll(z => z.Id != k.Id).ConvertAll(z => z.Id));
        var alterStadtvorplan = VersorgungstrassenPlan.PlaneFolge(stadtVorweg, out _);
        Pruefe(alterStadtvorplan.Count > 0 && !alterStadtvorplan[0].ZielEigene
            && !VersorgungstrassenPlan.PruefeVorplan(ZonenOhneStadtpfad(),
                alterStadtvorplan[0], out _, out _),
            "Mutation: Istplanung verwirft Stadtvorplan, wenn eigene Trasse frei wurde");
        Versorgungseingabe GetrennteZonen()
        {
            // Zwei Zonen, dazwischen eine fremde Leitung ueber die ganze
            // Laenge: kein eigener Weg, nur Zone 0 erreicht die Stadt.
            var e = ZonenOhneStadtpfad();
            e.Eigene.RemoveAt(2); e.Hinderniskanten.RemoveAt(2); e.Ziele.RemoveAt(2);
            var wand = Kante(44, 15, false, -200, 230); wand.Querbar = false;
            e.Leitungen.Add(wand);
            return e;
        }
        var getrennt = VersorgungstrassenPlan.PlaneFolge(GetrennteZonen(), out _);
        Pruefe(getrennt.Count > 0 && !getrennt[0].ZielEigene && getrennt[0].EigeneVergeblich
            && VersorgungstrassenPlan.PruefeVorplan(GetrennteZonen(), getrennt[0], out _, out _),
            "Stadtvorplan nach vergeblicher eigener Suche wird ohne zweite Wahl angenommen");
        var schonAngeschlossen = new Versorgungseingabe { Strombreite = 1,
            Wasserbreite = 1, Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
        var gasse = Kante(50, 20, false, -20, 0);
        gasse.Gasse = true; gasse.KnotenAnStadt = true;
        var zoneMitGasse = Kante(51, 20, false, 0, 30);
        zoneMitGasse.Startknoten = gasse.Endknoten;
        var andereZone = Kante(52, 50, false, 0, 30);
        schonAngeschlossen.Eigene.AddRange(new[] { gasse, zoneMitGasse, andereZone });
        schonAngeschlossen.Hinderniskanten.AddRange(schonAngeschlossen.Eigene);
        schonAngeschlossen.Ziele.AddRange(schonAngeschlossen.Eigene);
        schonAngeschlossen.Ziele.Add(Kante(53, -20, true, 0, 30));
        var gassenFolge = VersorgungstrassenPlan.PlaneFolge(schonAngeschlossen, out _);
        Pruefe(gassenFolge.Count == 1 && gassenFolge[0].ZielEigene
            && gassenFolge[0].ZielId == zoneMitGasse.Id,
            "Gasse am Stadtknoten: nur Verbindung zur anderen Zone");
        Pruefe(!VersorgungstrassenPlan.ZielErlaubt(false, 1, 0, true)
            && VersorgungstrassenPlan.ZielErlaubt(false, 1, 0),
            "Mutation: Gasse darf auch bei fremdem Teil kein Leitungsziel sein");
        var kurzeKante = Kante(60, 20, true, 0, 6);
        var langeKante = Kante(61, 20, true, 0, 30);
        Pruefe(!VersorgungstrassenPlan.SpurendeImKanteninneren(kurzeKante,
                new float2(20, 3))
            && !VersorgungstrassenPlan.SpurendeImKanteninneren(langeKante,
                new float2(20, 1))
            && VersorgungstrassenPlan.SpurendeImKanteninneren(langeKante,
                new float2(20, 10)),
            "Mutation: Projektion nahe Kantenende gesperrt, innen erlaubt");
        var kurzerAnschluss = new Versorgungseingabe { Strombreite = 1,
            Wasserbreite = 1, Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
        kurzerAnschluss.Eigene.Add(Kante(62, 0, false, 0, 30));
        kurzerAnschluss.Hinderniskanten.AddRange(kurzerAnschluss.Eigene);
        kurzerAnschluss.Ziele.Add(kurzeKante);
        Pruefe(VersorgungstrassenPlan.Waehle(kurzerAnschluss).Beste == null,
            "Mutation: kurze Zielkante akzeptiert keine Spur hinter ihrem Ende");
        var nurKnoten = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
            Sicherheitszugabe = 0.5f, Anschlussbereich = 8, NurKnotenziele = true };
        nurKnoten.Eigene.Add(Kante(70, 0, false, 10, 40));
        nurKnoten.Hinderniskanten.AddRange(nurKnoten.Eigene);
        var knotenziel = Kante(71, 20, true, 0, 60);
        nurKnoten.Ziele.Add(knotenziel);
        var knotenwahl = VersorgungstrassenPlan.Waehle(nurKnoten).Beste;
        Pruefe(knotenwahl != null && VersorgungsknotenDiagnose.IstEndpunkt(knotenziel, knotenwahl.Ziel.xz),
            "Diagnose: vorhandener Endknoten wird gefunden, keine stille Nichtstun-Loesung");
        Pruefe(!VersorgungsknotenDiagnose.IstEndpunkt(knotenziel, new float2(20, 30)),
            "Diagnose: Kantenmitte ist kein Zielknoten");
        var ohneKnoten = Kante(72, 20, true, 0, 60);
        ohneKnoten.Startknoten = ohneKnoten.Endknoten = 0;
        Pruefe(new List<float2>(VersorgungsknotenDiagnose.Ziele(ohneKnoten)).Count == 0,
            "Diagnose: keine erfundenen Knoten fuer fehlende Entity-Enden");
        var zoningprobe = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
            Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
        var gesperrteZone = Kante(80, 0, false, 0, 30);
        zoningprobe.Eigene.Add(gesperrteZone);
        zoningprobe.Hinderniskanten.Add(gesperrteZone);
        zoningprobe.Ziele.Add(Kante(82, 20, true, 0, 60));
        var alterVorplan = VersorgungstrassenPlan.PlaneFolge(zoningprobe, out _);
        Pruefe(alterVorplan.Count == 1, "Zoningprobe: Normalbetrieb findet die vorhandene Trasse");
        // PlaneFolge merkt die geplanten Verbindungen im Schnappschuss.
        // Der Vergleich soll denselben noch unversorgten Ausgangsstand lesen.
        zoningprobe.Verbindungen.Clear();
        gesperrteZone.AnschlussGesperrt = true;
        var gesperrteWahl = VersorgungstrassenPlan.Waehle(zoningprobe);
        Pruefe(gesperrteWahl.Beste == null && gesperrteWahl.Gruppen.Count == 1
            && gesperrteWahl.OffeneTeile == 1 && gesperrteWahl.Gruppen[0].Kanten.Count == 1,
            "Zoningprobe: Gruppe und offener Bedarf bleiben trotz 0 zulaessiger Starts erhalten");
        Pruefe(alterVorplan.Count == 1 && !VersorgungstrassenPlan.PruefeVorplan(zoningprobe,
            alterVorplan[0], out _, out _), "Zoningprobe: alter Vorplan umgeht die Startsperre nicht");
        var alternative = Kante(81, 0, false, 30, 60);
        alternative.Startknoten = gesperrteZone.Endknoten;
        zoningprobe.Eigene.Add(alternative); zoningprobe.Hinderniskanten.Add(alternative);
        var alternativeWahl = VersorgungstrassenPlan.Waehle(zoningprobe).Beste;
        Pruefe(alternativeWahl != null && alternativeWahl.Start.z > 30.1f
            && !alternativeWahl.Startkanten.Contains(gesperrteZone.Id),
            "Zoningprobe: gueltige Alternative gefunden, auch gemeinsamer Zoningknoten gesperrt");
        zoningprobe.Ziele[0].AnschlussGesperrt = true;
        Pruefe(VersorgungstrassenPlan.Waehle(zoningprobe).Beste == null,
            "Zoningprobe: gesperrte Kante wird auch als Ziel abgewiesen");
        var bushaltprobe = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
            Sicherheitszugabe = .5f, Anschlussbereich = 8 };
        var buskante = Kante(90, 0, false, 0, 30);
        var busfortsetzung = Kante(91, 0, false, 30, 60);
        busfortsetzung.Startknoten = buskante.Endknoten;
        bushaltprobe.Eigene.AddRange(new[] { buskante, busfortsetzung });
        bushaltprobe.Hinderniskanten.AddRange(bushaltprobe.Eigene);
        bushaltprobe.Ziele.Add(Kante(92, 20, true, 0, 60));
        var busvorplan = VersorgungstrassenPlan.PlaneFolge(bushaltprobe, out _);
        Pruefe(busvorplan.Count == 1, "Bushaltprobe: ohne Schalter wird der gueltige Anschluss gebaut");
        bushaltprobe.Verbindungen.Clear();
        bushaltprobe.Bushaltkanten.Add(buskante.Id);
        Pruefe(!VersorgungstrassenPlan.PruefeVorplan(bushaltprobe, busvorplan[0], out _, out _),
            "Bushaltprobe: frische Halt-Sperre verwirft alten Vorplan vor erneuter Wahl");
        var buswahl = VersorgungstrassenPlan.Waehle(bushaltprobe);
        Pruefe(buswahl.Beste == null && buswahl.OffeneTeile == 1 && buswahl.Gruppen.Count == 1,
            "Bushaltprobe: ganzes Halt-Netz hat 0 Trassen, Bedarf bleibt 1");
        Pruefe(buskante.AnschlussGesperrt && busfortsetzung.AnschlussGesperrt,
            "Bushaltprobe: Fortsetzung ohne Schild bleibt Teil der gesperrten Gruppe");
        var bustarget = Kante(91, 0, false, 30, 60);
        bushaltprobe.Ziele.Add(bustarget);
        var busalternative = Kante(93, 40, false, 0, 30);
        bushaltprobe.Eigene.Add(busalternative); bushaltprobe.Hinderniskanten.Add(busalternative);
        buswahl = VersorgungstrassenPlan.Waehle(bushaltprobe);
        Pruefe(bustarget.AnschlussGesperrt && !busalternative.AnschlussGesperrt,
            "Bushaltprobe: separater Zieleintrag gesperrt, andere Gruppe bleibt frei");
        Pruefe(buswahl.Beste != null && buswahl.Beste.Gruppe.Kanten.Contains(busalternative)
            && buswahl.Beste.Zielkante.Id == 92 && buswahl.OffeneTeile == 2,
            "Bushaltprobe: gueltige Alternative MUSS trotz gesperrtem Halt-Netz gefunden werden");
        Console.WriteLine($"Versorgungskurse: {pruefungen} Pruefungen, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
