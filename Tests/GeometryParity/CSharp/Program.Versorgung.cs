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
        // Nachstellung des Falls 26.09.: zwei Netze, das erste schon per
        // Knoten an der Stadt, das zweite mit einer 43,702-m-Geraden.
        Versorgungskante Kante(int id, float x, bool stadt = false,
            float z0 = 0, float z1 = 10)
        {
            var a = new float3(x, 0, z0); var b = new float3(x, 0, z1);
            return new Versorgungskante { Id = id, Startknoten = id * 2,
                Endknoten = id * 2 + 1, Startpunkt = a, Endpunkt = b,
                SteuerungB = math.lerp(a, b, 1f / 3).xz,
                SteuerungC = math.lerp(a, b, 2f / 3).xz,
                Position = t => math.lerp(a, b, t),
                Projektion = p => {
                    var q = Versorgungsnetz.Projektion(p, a.xz, b.xz);
                    return new float3(q.x, 0, q.y);
                },
                Breite = stadt ? 8 : 4, Versorgung = true,
                Stadt = stadt, Stromtor = true, Wassertor = true,
                Stromfang = 1, Wasserfang = 1 };
        }
        var ePlan = new Versorgungseingabe { Strombreite = 1, Wasserbreite = 1,
            Sicherheitszugabe = 0.5f, Anschlussbereich = 8 };
        var erstes = Kante(1, -100); erstes.KnotenAnStadt = true;
        var zweites = Kante(2, 0);
        var stadtziel = Kante(3, 43.702f, true, 10, 11);
        ePlan.Eigene.Add(erstes); ePlan.Eigene.Add(zweites);
        ePlan.Hinderniskanten.Add(erstes); ePlan.Hinderniskanten.Add(zweites);
        ePlan.Ziele.Add(erstes); ePlan.Ziele.Add(zweites); ePlan.Ziele.Add(stadtziel);
        var plan = VersorgungstrassenPlan.Waehle(ePlan);
        Pruefe(plan.Gruppen.Count == 2 && plan.PerKnotenAnStadt == 1
            && plan.OffeneTeile == 1, "zwei Netze, eines per Knoten an Stadt");
        Pruefe(plan.Beste != null && plan.Beste.Zielkante == stadtziel
            && math.abs(plan.Beste.Laenge - 43.702f) < 0.01f
            && plan.Beste.Startknoten != 0,
            "gemeinsamer Kern findet 43,702-m-Trasse vom zweiten Netz");
        Console.WriteLine($"Versorgungskurse: {pruefungen} Pruefungen, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
