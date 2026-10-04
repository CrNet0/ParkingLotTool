using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--laternen`: der Laternenplan (Geometry/ParkingLanterns) gegen die sechs
 * Bauzettel aus dem Design "PLT Laternenplanung" (Tests/GeometryParity/Laternen).
 *
 * Erwartet ist jede Laterne des JS-Planers aus dem Design bei 20, 30 und 40 m:
 * gleiche Art, Lage auf 5 cm, Richtung parallel (Rand: gleiche Richtung).
 * Dazu drei Gegenproben:
 * - Zittern: alle Koordinaten +-0,5 mm - das Ergebnis darf sich nicht aendern
 *   (genau daran zerfielen am 2026-10-04 die Reihen).
 * - Ohne Gras: dann gibt es keine Kappen - der Vergleich MUSS scheitern.
 * - Ohne Grundstuecksumriss: dann ist nichts aussen, es gibt keine einseitigen
 *   Randlaternen - der Vergleich MUSS scheitern.
 * - Verschobene Erwartung: eine Laterne 1 m daneben - MUSS scheitern.
 */
internal static partial class Program
{
    private sealed class LaternenFall
    {
        internal string Name;
        internal ParkingLayout Layout;
        internal Dictionary<int, List<(float2 P, string Art, float2 R)>> Erwartet = new();
    }

    private static int PruefeLaternen()
    {
        var ordner = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Laternen");
        if (!Directory.Exists(ordner)) ordner = Path.Combine("..", "Laternen");
        var faelle = Directory.GetFiles(ordner, "*.json").OrderBy(f => f).Select(LiesLaternenFall).ToList();
        int fehler = 0;
        foreach (var fall in faelle)
        foreach (var (abstand, soll) in fall.Erwartet)
        {
            var ist = ParkingLanterns.Plan(fall.Layout, abstand).Laternen;
            var befund = VergleicheLaternen(soll, ist);
            var zahlen = string.Join(" ", ist.GroupBy(l => l.Art).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()));
            Console.WriteLine($"{fall.Name} @{abstand} m: {ist.Count} Laternen ({zahlen}) - {(befund ?? "ok")}");
            if (befund != null) fehler++;
        }

        // Gegenprobe 1: Zittern um +-0,5 mm darf nichts aendern. Fuenf Startwerte
        // und alle drei Abstaende: ein einzelner Lauf fand nur die Kanten, die sein
        // Zufall gerade traf (2026-10-04: die 13,5-m-Ausduennung erst im dritten Anlauf).
        foreach (var fall in faelle)
        {
            string erster = null;
            var laeufe = 0;
            foreach (var saat in new[] { 4711, 1, 2, 3, 4 })
            {
                var zufall = new System.Random(saat);
                float Z() => (float)(zufall.NextDouble() - 0.5) * 0.001f;
                var l = fall.Layout;
                var zitter = NeuesLayout(
                    l.Bay.Select(p => p.Select(q => q + new float2(Z(), Z())).ToArray()).ToArray(),
                    l.GrassSurface.Select(r => r.Select(q => q + new float2(Z(), Z())).ToArray()).ToArray(),
                    l.AsphaltSurface.Select(r => r.Select(q => q + new float2(Z(), Z())).ToArray()).ToArray(),
                    l.AisleLine, l.CrossLine, l.NetLine,
                    l.Grundstueck.Select(q => q + new float2(Z(), Z())).ToArray());
                foreach (var (abstand, soll) in fall.Erwartet)
                {
                    laeufe++;
                    var befund = VergleicheLaternen(soll, ParkingLanterns.Plan(zitter, abstand).Laternen, 0.1f);
                    if (befund != null && erster == null) erster = $"Saat {saat} @{abstand} m: {befund}";
                }
            }
            Console.WriteLine($"Zittern {fall.Name} ({laeufe} Laeufe): {(erster ?? "unveraendert")}");
            if (erster != null) fehler++;
        }

        // Gegenprobe 2: ohne Gras keine Kappen - muss auffallen
        var mitKappen = faelle.First(f => f.Erwartet[30].Any(x => x.Art == "Kappe"));
        var ohneGras = NeuesLayout(mitKappen.Layout.Bay, Array.Empty<float2[]>(), mitKappen.Layout.AsphaltSurface, mitKappen.Layout.AisleLine,
            mitKappen.Layout.CrossLine, mitKappen.Layout.NetLine, mitKappen.Layout.Grundstueck);
        var probe2 = VergleicheLaternen(mitKappen.Erwartet[30], ParkingLanterns.Plan(ohneGras, 30).Laternen);
        Console.WriteLine($"Gegenprobe ohne Gras ({mitKappen.Name}): {(probe2 != null ? "faellt auf - " + probe2 : "UNBEMERKT")}");
        if (probe2 == null) fehler++;

        // Gegenprobe 3: eine Erwartung 1 m verschoben - muss auffallen
        var verschoben = mitKappen.Erwartet[30].ToList();
        verschoben[0] = (verschoben[0].P + new float2(1, 0), verschoben[0].Art, verschoben[0].R);
        var probe3 = VergleicheLaternen(verschoben, ParkingLanterns.Plan(mitKappen.Layout, 30).Laternen);
        Console.WriteLine($"Gegenprobe verschobene Erwartung: {(probe3 != null ? "faellt auf" : "UNBEMERKT")}");
        if (probe3 == null) fehler++;

        // Gegenprobe 4: ohne Umriss ist nichts aussen - muss auffallen
        var mitRand = faelle.First(f => f.Erwartet[30].Any(x => x.Art == "Rand"));
        var ohneUmriss = NeuesLayout(mitRand.Layout.Bay, mitRand.Layout.GrassSurface, mitRand.Layout.AsphaltSurface, mitRand.Layout.AisleLine,
            mitRand.Layout.CrossLine, mitRand.Layout.NetLine, Array.Empty<float2>());
        var probe4 = VergleicheLaternen(mitRand.Erwartet[30], ParkingLanterns.Plan(ohneUmriss, 30).Laternen);
        Console.WriteLine($"Gegenprobe ohne Umriss ({mitRand.Name}): {(probe4 != null ? "faellt auf - " + probe4 : "UNBEMERKT")}");
        if (probe4 == null) fehler++;

        fehler += PruefeLaternenkollision();

        Console.WriteLine(fehler == 0 ? "LATERNEN OK" : $"LATERNEN: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    /**
     * Die nachgerechnete CS2-Pruefung (Geometry/Laternenkollision) an einer
     * Doppellaterne mit den Massen aus der Inventur (StreetlightDouble02, Arme
     * +-1,9 m, Mast 0,3 m; Beinhoehe hier angenommen 5 m). Prueft unsere
     * Umsetzung, nicht CS2 selbst: Mast, Arme nur fuer hohe Pflanzen, Drehung.
     */
    private static int PruefeLaternenkollision()
    {
        LaternenKoerper Doppel(float2 vorn) => new LaternenKoerper
        {
            Position = new float2(100, 200), Vorwaerts = vorn, Stehend = true, RundesBein = true,
            Bein = new float3(0.3f, 5f, 0.3f), Min = new float3(-1.9f, 0, -0.15f), Max = new float3(1.9f, 7.02f, 0.15f),
        };
        var k = Doppel(new float2(0, 1));
        var o = k.Position;
        var faelle = new (string Name, LaternenKoerper K, float2 P, float R, float H, bool Soll)[]
        {
            ("niedriger Busch am Mast", k, o + new float2(0.6f, 0), 0.5f, 1f, true),
            ("niedriger Busch knapp frei", k, o + new float2(0.75f, 0), 0.5f, 1f, false),
            ("niedriger Busch unter dem Arm", k, o + new float2(1.5f, 0), 0.5f, 1f, false),
            ("hoher Busch am Armende", k, o + new float2(2.3f, 0), 0.5f, 6f, true),
            ("hoher Busch quer zum Arm", k, o + new float2(0, 0.8f), 0.5f, 6f, false),
            ("gedreht: Arme laufen in Z", Doppel(new float2(1, 0)), o + new float2(0, 2.3f), 0.5f, 6f, true),
            ("gedreht: quer dazu frei", Doppel(new float2(1, 0)), o + new float2(2.3f, 0), 0.5f, 6f, false),
        };
        var fehler = 0;
        foreach (var f in faelle)
        {
            var ist = Laternenkollision.Beruehrt(f.K, f.P, f.R, f.H);
            if (ist != f.Soll) { fehler++; Console.WriteLine($"Kollision {f.Name}: {ist} statt {f.Soll}"); }
        }
        Console.WriteLine($"Laternenkollision: {faelle.Length - fehler} von {faelle.Length} richtig");
        return fehler;
    }

    private static string VergleicheLaternen(List<(float2 P, string Art, float2 R)> soll, List<LaternenPlatz> ist, float tol = 0.05f)
    {
        if (soll.Count != ist.Count)
        {
            // Welche fehlt oder ist zu viel: sonst sucht man die Kante blind.
            var zuViel = ist.Where(x => !soll.Any(s => math.distance(x.Position, s.P) < tol && x.Art.ToString() == s.Art));
            var fehlt = soll.Where(s => !ist.Any(x => math.distance(x.Position, s.P) < tol && x.Art.ToString() == s.Art));
            return $"Anzahl {ist.Count} statt {soll.Count}"
                + string.Concat(zuViel.Take(3).Select(x => $"; zu viel {x.Art} ({x.Position.x:F2}/{x.Position.y:F2})"))
                + string.Concat(fehlt.Take(3).Select(s => $"; fehlt {s.Art} ({s.P.x:F2}/{s.P.y:F2})"));
        }
        var frei = ist.ToList();
        foreach (var s in soll)
        {
            var i = frei.FindIndex(x => math.distance(x.Position, s.P) < tol && x.Art.ToString() == s.Art);
            if (i < 0) return $"keine {s.Art} bei ({s.P.x:F2}/{s.P.y:F2})";
            var r = frei[i].Richtung;
            var kreuz = math.abs(r.x * s.R.y - r.y * s.R.x);
            if (kreuz > 0.02f || (s.Art == "Rand" && math.dot(r, s.R) < 0)) return $"{s.Art} bei ({s.P.x:F2}/{s.P.y:F2}) falsch gedreht";
            frei.RemoveAt(i);
        }
        return null;
    }

    private static ParkingLayout NeuesLayout(float2[][] bay, float2[][] gras, float2[][] asphalt, float2[][] aisle, float2[][] cross,
        NetSegment[] netze, float2[] grundstueck)
        => new ParkingLayout { Bay = bay, GrassSurface = gras, AsphaltSurface = asphalt, AisleLine = aisle, CrossLine = cross,
            NetLine = netze, Grundstueck = grundstueck };

    private static LaternenFall LiesLaternenFall(string pfad)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
        var wurzel = doc.RootElement;
        var l = wurzel.GetProperty("Layout");
        float2 P(JsonElement e) => new float2(e[0].GetSingle(), e[1].GetSingle());
        float2[][] Ringe(string name) => l.GetProperty(name).EnumerateArray()
            .Select(r => r.EnumerateArray().Select(P).ToArray()).ToArray();
        var netze = l.GetProperty("NetLine").EnumerateArray()
            .Select(n => new NetSegment(n.GetProperty("Kind").GetString(), P(n.GetProperty("A")), P(n.GetProperty("B")))).ToArray();
        var fall = new LaternenFall
        {
            Name = Path.GetFileNameWithoutExtension(pfad),
            Layout = NeuesLayout(Ringe("Bay"), Ringe("GrassSurface"), Ringe("AsphaltSurface"), Ringe("AisleLine"), Ringe("CrossLine"), netze,
                l.GetProperty("Grundstueck").EnumerateArray().Select(P).ToArray()),
        };
        foreach (var e in wurzel.GetProperty("Erwartet").EnumerateObject())
            fall.Erwartet[int.Parse(e.Name)] = e.Value.EnumerateArray().Select(x => (
                new float2(x.GetProperty("X").GetSingle(), x.GetProperty("Z").GetSingle()),
                x.GetProperty("Art").GetString(),
                new float2(x.GetProperty("RX").GetSingle(), x.GetProperty("RZ").GetSingle()))).ToList();
        return fall;
    }
}
