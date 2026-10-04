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

        // Gegenprobe 1: Zittern um +-0,5 mm darf nichts aendern
        var zufall = new System.Random(4711);
        float Z() => (float)(zufall.NextDouble() - 0.5) * 0.001f;
        foreach (var fall in faelle)
        {
            var l = fall.Layout;
            var zitter = NeuesLayout(
                l.Bay.Select(p => p.Select(q => q + new float2(Z(), Z())).ToArray()).ToArray(),
                l.GrassSurface.Select(r => r.Select(q => q + new float2(Z(), Z())).ToArray()).ToArray(),
                l.AisleLine, l.CrossLine, l.NetLine);
            var befund = VergleicheLaternen(fall.Erwartet[30], ParkingLanterns.Plan(zitter, 30).Laternen, 0.1f);
            Console.WriteLine($"Zittern {fall.Name}: {(befund ?? "unveraendert")}");
            if (befund != null) fehler++;
        }

        // Gegenprobe 2: ohne Gras keine Kappen - muss auffallen
        var mitKappen = faelle.First(f => f.Erwartet[30].Any(x => x.Art == "Kappe"));
        var ohneGras = NeuesLayout(mitKappen.Layout.Bay, Array.Empty<float2[]>(), mitKappen.Layout.AisleLine,
            mitKappen.Layout.CrossLine, mitKappen.Layout.NetLine);
        var probe2 = VergleicheLaternen(mitKappen.Erwartet[30], ParkingLanterns.Plan(ohneGras, 30).Laternen);
        Console.WriteLine($"Gegenprobe ohne Gras ({mitKappen.Name}): {(probe2 != null ? "faellt auf - " + probe2 : "UNBEMERKT")}");
        if (probe2 == null) fehler++;

        // Gegenprobe 3: eine Erwartung 1 m verschoben - muss auffallen
        var verschoben = mitKappen.Erwartet[30].ToList();
        verschoben[0] = (verschoben[0].P + new float2(1, 0), verschoben[0].Art, verschoben[0].R);
        var probe3 = VergleicheLaternen(verschoben, ParkingLanterns.Plan(mitKappen.Layout, 30).Laternen);
        Console.WriteLine($"Gegenprobe verschobene Erwartung: {(probe3 != null ? "faellt auf" : "UNBEMERKT")}");
        if (probe3 == null) fehler++;

        Console.WriteLine(fehler == 0 ? "LATERNEN OK" : $"LATERNEN: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }

    private static string VergleicheLaternen(List<(float2 P, string Art, float2 R)> soll, List<LaternenPlatz> ist, float tol = 0.05f)
    {
        if (soll.Count != ist.Count) return $"Anzahl {ist.Count} statt {soll.Count}";
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

    private static ParkingLayout NeuesLayout(float2[][] bay, float2[][] gras, float2[][] aisle, float2[][] cross, NetSegment[] netze)
        => new ParkingLayout { Bay = bay, GrassSurface = gras, AisleLine = aisle, CrossLine = cross, NetLine = netze };

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
            Layout = NeuesLayout(Ringe("Bay"), Ringe("GrassSurface"), Ringe("AisleLine"), Ringe("CrossLine"), netze),
        };
        foreach (var e in wurzel.GetProperty("Erwartet").EnumerateObject())
            fall.Erwartet[int.Parse(e.Name)] = e.Value.EnumerateArray().Select(x => (
                new float2(x.GetProperty("X").GetSingle(), x.GetProperty("Z").GetSingle()),
                x.GetProperty("Art").GetString(),
                new float2(x.GetProperty("RX").GetSingle(), x.GetProperty("RZ").GetSingle()))).ToList();
        return fall;
    }
}
