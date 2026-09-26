using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Alle Funktionen auf Kante lesen nur den beim Aufruf eingefrorenen Kurvenstand.
    // So bleibt die Wahl auch auf dem Vorplan-Hintergrundfaden ECS-frei.
    internal sealed class Versorgungskante
    {
        internal int Id, Startknoten, Endknoten;
        internal float3 Startpunkt, Endpunkt;
        internal float2 SteuerungB, SteuerungC;
        internal Func<float, float3> Position;
        internal Func<float2, float3> Projektion;
        internal float Breite, Stromfang, Wasserfang;
        internal bool Versorgung, Querbar, Stadt, NurEnden, Stromtor, Wassertor;
        internal bool FlussAnStadt, KnotenAnStadt;

        internal float Abstand(float2 p)
        {
            if (NurEnden) return math.min(math.distance(Position(0).xz, p),
                math.distance(Position(1).xz, p));
            return math.distance(Projektion(p).xz, p);
        }
    }

    internal sealed class Versorgungseingabe
    {
        internal readonly List<Versorgungskante> Eigene = new List<Versorgungskante>();
        internal readonly List<Versorgungskante> Hinderniskanten = new List<Versorgungskante>();
        internal readonly List<Versorgungskante> Ziele = new List<Versorgungskante>();
        internal readonly List<Versorgungskante> Leitungen = new List<Versorgungskante>();
        internal readonly List<(float2 Start, float2 Ziel, bool Stadt)> Verbindungen =
            new List<(float2, float2, bool)>();
        internal readonly Dictionary<int, HashSet<int>> GesperrteZiele =
            new Dictionary<int, HashSet<int>>();
        internal readonly HashSet<int> Ausgereizt = new HashSet<int>();
        internal float Strombreite, Wasserbreite;
        internal float Suchgrenze = float.MaxValue;
        internal float Sicherheitszugabe, Anschlussbereich;
        internal Func<bool> Abgebrochen;
    }

    internal sealed class Versorgungsauswahl
    {
        internal readonly List<Versorgungsgruppe> Gruppen = new List<Versorgungsgruppe>();
        internal Versorgungstrassenweg Beste;
        internal int OffeneTeile, PerKnotenAnStadt, OhneVersorgung;
        internal int EigeneKanten, AlleZiele, Hinderniskanten, Fremdleitungen;
    }

    internal sealed class Versorgungsgruppe
    {
        internal readonly List<Versorgungskante> Kanten = new List<Versorgungskante>();
        internal readonly List<Versorgungskante> Ziele = new List<Versorgungskante>();
        internal readonly List<float3> Starts = new List<float3>();
        internal bool AnStadt, Ausgereizt;
        internal int Teil, Gesperrt;
        internal Versorgungsweg.Ergebnis Gerade, Umweg;
        internal int Huellen, Ecken, QuerbareHuellen;
        internal double UmwegMs;
        internal float Suchgrenze;
        internal Versorgungstrassenweg Weg;
    }

    internal sealed class Versorgungstrassenweg
    {
        internal Versorgungskante Zielkante;
        internal Versorgungsgruppe Gruppe;
        internal float3 Start, Ziel;
        internal int Startknoten;
        internal List<int> Startkanten;
        internal List<float2> Stromweg, Wasserweg;
        internal float Laenge;
        internal bool Hindernisweg;
    }

    internal static class VersorgungstrassenPlan
    {
        internal static bool ZielErlaubt(bool stadt, int zielTeil, int meinTeil)
            => stadt || (zielTeil >= 0 && zielTeil != meinTeil);

        internal static Versorgungsauswahl Waehle(Versorgungseingabe e)
        {
            var aus = new Versorgungsauswahl();
            aus.EigeneKanten = e.Eigene.Count;
            aus.AlleZiele = e.Ziele.Count;
            aus.Hinderniskanten = e.Hinderniskanten.Count;
            aus.Fremdleitungen = e.Leitungen.Count;
            var tragende = e.Eigene.FindAll(k => k.Versorgung);
            aus.OhneVersorgung = e.Eigene.Count - tragende.Count;
            aus.Gruppen.AddRange(Gruppen(tragende, e.Abgebrochen));
            var wurzel = new int[aus.Gruppen.Count + 1];
            for (var i = 0; i < wurzel.Length; i++) wurzel[i] = i;
            var stadt = aus.Gruppen.Count;
            int Finde(int i)
            {
                while (wurzel[i] != i) { wurzel[i] = wurzel[wurzel[i]]; i = wurzel[i]; }
                return i;
            }
            void Verbinde(int a, int b)
            {
                a = Finde(a); b = Finde(b);
                if (a == b) return;
                if (b == Finde(stadt)) wurzel[a] = b; else wurzel[b] = a;
            }
            int GruppeAmPunkt(float2 p)
            {
                for (var i = 0; i < aus.Gruppen.Count; i++)
                    foreach (var k in aus.Gruppen[i].Kanten)
                        if (math.distance(k.Projektion(p).xz, p) <= 0.1f) return i;
                return -1;
            }
            var gruppeVon = new Dictionary<int, int>();
            for (var i = 0; i < aus.Gruppen.Count; i++)
            {
                var g = aus.Gruppen[i];
                foreach (var k in g.Kanten) gruppeVon[k.Id] = i;
                var amStadtknoten = g.Kanten.Exists(k => k.KnotenAnStadt);
                if (amStadtknoten) aus.PerKnotenAnStadt++;
                if (amStadtknoten || g.Kanten.TrueForAll(k => k.FlussAnStadt))
                    Verbinde(i, stadt);
            }
            foreach (var v in e.Verbindungen)
            {
                var a = GruppeAmPunkt(v.Start);
                var b = v.Stadt ? stadt : GruppeAmPunkt(v.Ziel);
                if (a >= 0 && b >= 0) Verbinde(a, b);
            }
            var teile = new HashSet<int>();
            for (var i = 0; i < aus.Gruppen.Count; i++)
            {
                var g = aus.Gruppen[i];
                g.Teil = Finde(i); g.AnStadt = g.Teil == Finde(stadt);
                if (!g.AnStadt) teile.Add(g.Teil);
            }
            aus.OffeneTeile = teile.Count;

            foreach (var g in aus.Gruppen)
            {
                PruefeAbbruch(e);
                if (g.AnStadt) continue;
                g.Ausgereizt = e.Ausgereizt.Contains(g.Kanten[0].Id);
                if (g.Ausgereizt) continue;
                e.GesperrteZiele.TryGetValue(g.Kanten[0].Id, out var gesperrt);
                g.Gesperrt = gesperrt?.Count ?? 0;
                foreach (var z in e.Ziele)
                {
                    if (g.Kanten.Contains(z) || gesperrt?.Contains(z.Id) == true) continue;
                    var zielTeil = gruppeVon.TryGetValue(z.Id, out var i) ? Finde(i) : -1;
                    if (ZielErlaubt(z.Stadt, zielTeil, g.Teil)) g.Ziele.Add(z);
                }
                if (g.Ziele.Count == 0) continue;
                g.Weg = WaehleGruppe(e, g, aus.Beste?.Laenge + 0.002f ?? e.Suchgrenze);
                if (g.Weg != null && (aus.Beste == null ||
                    g.Weg.Laenge < aus.Beste.Laenge)) aus.Beste = g.Weg;
            }
            return aus;
        }

        internal static List<Versorgungsgruppe> Gruppen(List<Versorgungskante> tragende,
            Func<bool> abgebrochen = null)
        {
            var gruppen = new List<Versorgungsgruppe>();
            var offen = new HashSet<Versorgungskante>(tragende);
            var amKnoten = new Dictionary<int, List<Versorgungskante>>();
            foreach (var k in tragende)
                foreach (var n in new[] { k.Startknoten, k.Endknoten })
                {
                    if (n == 0) continue;
                    if (!amKnoten.TryGetValue(n, out var liste))
                        amKnoten[n] = liste = new List<Versorgungskante>();
                    liste.Add(k);
                }
            while (offen.Count > 0)
            {
                if (abgebrochen?.Invoke() == true) throw new OperationCanceledException();
                Versorgungskante erste = null;
                foreach (var k in offen) { erste = k; break; }
                offen.Remove(erste);
                var g = new Versorgungsgruppe();
                g.Kanten.Add(erste);
                var stapel = new List<Versorgungskante> { erste };
                while (stapel.Count > 0)
                {
                    var oben = stapel[stapel.Count - 1];
                    stapel.RemoveAt(stapel.Count - 1);
                    foreach (var n in new[] { oben.Startknoten, oben.Endknoten })
                        if (n != 0 && amKnoten.TryGetValue(n, out var liste))
                            foreach (var k in liste)
                                if (offen.Remove(k)) { g.Kanten.Add(k); stapel.Add(k); }
                }
                gruppen.Add(g);
            }
            return gruppen;
        }

        private static Versorgungstrassenweg WaehleGruppe(Versorgungseingabe e,
            Versorgungsgruppe g, float maxLaenge)
        {
            void Merke(Versorgungskante k, float2 p)
            {
                var punkt = k.Projektion(p);
                foreach (var alt in g.Starts)
                    if (math.distance(alt, punkt) < 0.01f) return;
                g.Starts.Add(punkt);
            }
            foreach (var k in g.Kanten)
            {
                PruefeAbbruch(e);
                Merke(k, k.Position(0.5f).xz);
                foreach (var z in g.Ziele)
                    foreach (var p in Versorgungsnetz.Kantenpunkte(t => k.Position(t).xz,
                        p => k.Projektion(p).xz, t => z.Position(t).xz,
                        p => z.Projektion(p).xz)) Merke(k, p);
            }
            var knoten = new HashSet<int>();
            foreach (var k in g.Kanten)
                foreach (var n in new[] { k.Startknoten, k.Endknoten })
                    if (n != 0 && knoten.Add(n))
                        g.Starts.Add(n == k.Startknoten ? k.Startpunkt : k.Endpunkt);

            var startstrassen = new HashSet<int>[g.Starts.Count];
            var startknoten = new int[g.Starts.Count];
            for (var i = 0; i < g.Starts.Count; i++)
            {
                var p = g.Starts[i];
                foreach (var k in g.Kanten)
                {
                    if (k.Startknoten != 0 && math.distance(k.Startpunkt, p) <= 0.1f)
                    { startknoten[i] = k.Startknoten; break; }
                    if (k.Endknoten != 0 && math.distance(k.Endpunkt, p) <= 0.1f)
                    { startknoten[i] = k.Endknoten; break; }
                }
                var ids = new HashSet<int>();
                foreach (var k in g.Kanten)
                    if ((startknoten[i] != 0 && (k.Startknoten == startknoten[i]
                        || k.Endknoten == startknoten[i]))
                        || math.distance(k.Projektion(p.xz).xz, p.xz) <= 0.1f) ids.Add(k.Id);
                startstrassen[i] = ids;
            }
            IEnumerable<Versorgungsweg.Ziel> Ziele(float2 p)
            {
                for (var i = 0; i < g.Ziele.Count; i++)
                {
                    var k = g.Ziele[i];
                    yield return new Versorgungsweg.Ziel { Index = i, Punkt = k.Projektion(p).xz };
                    for (var n = 0; n <= 16; n++)
                        yield return new Versorgungsweg.Ziel { Index = i,
                            Punkt = k.Position(n / 16f).xz };
                }
            }
            var achse = VersorgungskursPruefung.Achsabstand(e.Strombreite, e.Wasserbreite);
            var hindernisse = new List<Versorgungsweg.Hindernis>();
            var aufgeweitet = new List<Versorgungsweg.Hindernis>();
            void Huelle(List<Versorgungsweg.Hindernis> liste, Versorgungskante k, float zugabe)
            {
                Versorgungsweg.Bogen(liste, k.Id, k.Position(0).xz,
                    k.SteuerungB, k.SteuerungC, k.Position(1).xz,
                    k.Breite / 2 + e.Sicherheitszugabe + zugabe, querbar: k.Querbar);
            }
            var erweiterung = math.max(e.Strombreite, e.Wasserbreite) / 2 + achse;
            foreach (var k in e.Hinderniskanten)
            { PruefeAbbruch(e); Huelle(hindernisse, k, math.max(e.Strombreite, e.Wasserbreite) / 2);
                Huelle(aufgeweitet, k, erweiterung); }
            foreach (var k in e.Leitungen)
            { PruefeAbbruch(e); Huelle(hindernisse, k, math.max(e.Strombreite, e.Wasserbreite) / 2);
                Huelle(aufgeweitet, k, erweiterung); }
            g.Huellen = aufgeweitet.Count;
            foreach (var h in aufgeweitet)
            { g.Ecken += h.Ring.Length; if (h.Querbar) g.QuerbareHuellen++; }
            bool Tor(Versorgungskante k, float2 p, bool strom)
                => (strom ? k.Stromtor : k.Wassertor)
                    && VersorgungskursPruefung.Anschluss(k.Abstand(p), k.Breite,
                        strom ? e.Strombreite : e.Wasserbreite,
                        strom ? k.Stromfang : k.Wasserfang, true);
            bool Wege(List<float2> weg, int si, int zi,
                out List<float2> strom, out List<float2> wasser)
            {
                var ziel = g.Ziele[zi];
                return Versorgungsweg.Spuren(weg, e.Strombreite, e.Wasserbreite,
                    hindernisse, startstrassen[si], e.Anschlussbereich, out strom, out wasser,
                    (p, s) => Tor(ziel, p, s),
                    ziel.Stadt ? null : new HashSet<int> { ziel.Id },
                    (p, s) => g.Kanten.Exists(k => startstrassen[si].Contains(k.Id) && Tor(k, p, s)));
            }
            var punkte = g.Starts.ConvertAll(p => p.xz);
            g.Gerade = Versorgungsnetz.Gerade(punkte, Ziele,
                (i, z) => Wege(new List<float2> { punkte[i], z.Punkt }, i, z.Index,
                    out _, out _), maxLaenge, e.Abgebrochen);
            PruefeAbbruch(e);
            g.Suchgrenze = g.Gerade.Punkte == null ? maxLaenge
                : math.min(maxLaenge, g.Gerade.Laenge + 0.002f);
            var uhr = Stopwatch.StartNew();
            g.Umweg = Versorgungsweg.Suche(punkte, aufgeweitet, i => startstrassen[i], Ziele,
                (weg, zi) => {
                    var i = punkte.IndexOf(weg[0]);
                    return i >= 0 && Wege(weg, i, zi, out _, out _);
                }, e.Anschlussbereich + achse,
                zi => g.Ziele[zi].Stadt ? null : new HashSet<int> { g.Ziele[zi].Id },
                g.Suchgrenze,
                e.Abgebrochen);
            g.UmwegMs = uhr.Elapsed.TotalMilliseconds;
            PruefeAbbruch(e);
            var r = g.Umweg.Punkte != null && (g.Gerade.Punkte == null ||
                Versorgungsnetz.Kuerzer(g.Umweg.Laenge, g.Gerade.Laenge)) ? g.Umweg : g.Gerade;
            if (r.Punkte == null) return null;
            var zielkante = g.Ziele[r.Ziel];
            var weg = new Versorgungstrassenweg { Gruppe = g, Zielkante = zielkante,
                Start = g.Starts[r.Start], Ziel = zielkante.Projektion(r.Punkte[r.Punkte.Count - 1]),
                Startknoten = startknoten[r.Start], Laenge = r.Laenge,
                Hindernisweg = r == g.Umweg, Startkanten = new List<int>() };
            foreach (var k in g.Kanten)
                if (startstrassen[r.Start].Contains(k.Id)) weg.Startkanten.Add(k.Id);
            Wege(r.Punkte, r.Start, r.Ziel, out weg.Stromweg, out weg.Wasserweg);
            return weg;
        }

        private static void PruefeAbbruch(Versorgungseingabe e)
        {
            if (e.Abgebrochen?.Invoke() == true) throw new OperationCanceledException();
        }
    }
}
