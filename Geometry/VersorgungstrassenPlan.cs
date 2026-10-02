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
        internal bool Gasse;
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
        internal bool NurKnotenziele;
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
        internal List<float2> Punkte;
    }

    internal sealed class Versorgungsvorplan
    {
        internal int ZielId;
        internal bool ZielEigene;
        internal float2 Start, Ziel;
        internal float Laenge;
        internal bool Hindernisweg;
        internal List<float2> Punkte, Stromweg, Wasserweg;
        internal int OffeneTeile;
        internal bool AnderesNetzKuerzer;
        // Der Vorplan hat eigene Ziele gesucht, keines erreicht und erst
        // dann die Stadt genommen - ohne Sperren, die die Suche verengten.
        internal bool EigeneVergeblich;
    }

    internal static class VersorgungstrassenPlan
    {
        internal static List<Versorgungsvorplan> PlaneFolge(Versorgungseingabe e,
            out Versorgungsauswahl erste)
        {
            var folge = new List<Versorgungsvorplan>();
            erste = null;
            var limit = e.Eigene.Count;
            for (var i = 0; i < limit; i++)
            {
                PruefeAbbruch(e);
                var aus = Waehle(e);
                if (i == 0) erste = aus;
                var w = aus.Beste;
                if (w == null) break;
                var kuerzer = false;
                foreach (var g in aus.Gruppen)
                    if (!g.AnStadt && g != w.Gruppe && g.Weg != null
                        && g.Weg.Laenge < w.Laenge - 0.002f) kuerzer = true;
                folge.Add(new Versorgungsvorplan { ZielId = w.Zielkante.Id,
                    ZielEigene = !w.Zielkante.Stadt, Start = w.Start.xz, Ziel = w.Ziel.xz,
                    Laenge = w.Laenge, Hindernisweg = w.Hindernisweg,
                    Punkte = new List<float2>(w.Punkte), Stromweg = new List<float2>(w.Stromweg),
                    Wasserweg = new List<float2>(w.Wasserweg), OffeneTeile = aus.OffeneTeile,
                    AnderesNetzKuerzer = kuerzer,
                    EigeneVergeblich = w.Zielkante.Stadt && BrauchtEigenesZiel(aus)
                        && e.Ausgereizt.Count == 0
                        && OhneSperren(e) });
                // Dieselbe Merkung wie nach einem erfolgreichen Apply: die
                // Verbindung aendert die Teile vor der naechsten Wahl.
                e.Verbindungen.Add((w.Start.xz, w.Ziel.xz, w.Zielkante.Stadt));
            }
            return folge;
        }

        private static bool OhneSperren(Versorgungseingabe e)
        {
            foreach (var sperre in e.GesperrteZiele.Values)
                if (sperre.Count > 0) return false;
            return true;
        }

        internal static bool ZielErlaubt(bool stadt, int zielTeil, int meinTeil,
            bool gasse = false)
            => !gasse && (stadt || (zielTeil >= 0 && zielTeil != meinTeil));

        private static bool BrauchtEigenesZiel(Versorgungsauswahl aus)
            => aus.OffeneTeile > 1 || (aus.OffeneTeile == 1
                && aus.Gruppen.Exists(g => g.AnStadt));

        private static void Zielstufe(Versorgungsauswahl aus, bool stadt)
        {
            foreach (var g in aus.Gruppen)
            {
                g.Ziele.RemoveAll(k => k.Stadt != stadt);
                g.Weg = null;
            }
            aus.Beste = null;
        }

        internal static bool SpurendeImKanteninneren(Versorgungskante k, float2 p)
        {
            if (k.NurEnden) return true;
            var lot = k.Projektion(p).xz;
            var reserve = math.max(1f, k.Breite / 2);
            return math.distance(lot, k.Position(0).xz) + 0.001f >= reserve
                && math.distance(lot, k.Position(1).xz) + 0.001f >= reserve;
        }

        // Die Istpruefung und die Wahl verwenden dieselben Startkanten und Tore.
        private static HashSet<int> Startkanten(Versorgungsgruppe g, float3 p, out int startknoten)
        {
            startknoten = 0;
            foreach (var k in g.Kanten)
            {
                if (k.Startknoten != 0 && math.distance(k.Startpunkt, p) <= 0.1f)
                { startknoten = k.Startknoten; break; }
                if (k.Endknoten != 0 && math.distance(k.Endpunkt, p) <= 0.1f)
                { startknoten = k.Endknoten; break; }
            }
            var ids = new HashSet<int>();
            foreach (var k in g.Kanten)
                if ((startknoten != 0 && (k.Startknoten == startknoten
                    || k.Endknoten == startknoten))
                    || math.distance(k.Projektion(p.xz).xz, p.xz) <= 0.1f) ids.Add(k.Id);
            return ids;
        }

        private static void Hindernisse(Versorgungseingabe e,
            out List<Versorgungsweg.Hindernis> hindernisse,
            out List<Versorgungsweg.Hindernis> aufgeweitet)
        {
            hindernisse = new List<Versorgungsweg.Hindernis>();
            aufgeweitet = new List<Versorgungsweg.Hindernis>();
            var achse = VersorgungskursPruefung.Achsabstand(e.Strombreite, e.Wasserbreite);
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
        }

        private static bool Wege(Versorgungseingabe e, Versorgungsgruppe g,
            Versorgungskante ziel, HashSet<int> startstrassen,
            List<Versorgungsweg.Hindernis> hindernisse, List<float2> weg,
            out List<float2> strom, out List<float2> wasser)
        {
            bool Tor(Versorgungskante k, float2 p, bool istStrom)
                => (istStrom ? k.Stromtor : k.Wassertor)
                    // Beide Parallelspuren brauchen auf der Kante Platz. Bei
                    // Small Road (8 m) sind das 4 m je Ende; ein geklemmter
                    // Projektionspunkt direkt am Ende reicht CS2 nicht.
                    && (e.NurKnotenziele && k == ziel || SpurendeImKanteninneren(k, p))
                    && VersorgungskursPruefung.Anschluss(k.Abstand(p), k.Breite,
                        istStrom ? e.Strombreite : e.Wasserbreite,
                        istStrom ? k.Stromfang : k.Wasserfang, true);
            return Versorgungsweg.Spuren(weg, e.Strombreite, e.Wasserbreite,
                hindernisse, startstrassen, e.Anschlussbereich, out strom, out wasser,
                (p, s) => Tor(ziel, p, s),
                ziel.Stadt ? null : new HashSet<int> { ziel.Id },
                (p, s) => g.Kanten.Exists(k => startstrassen.Contains(k.Id) && Tor(k, p, s)));
        }

        internal static Versorgungsauswahl Waehle(Versorgungseingabe e)
        {
            var aus = Bereite(e);
            if (BrauchtEigenesZiel(aus))
            {
                foreach (var g in aus.Gruppen) g.Ziele.RemoveAll(k => k.Stadt);
                WaehleStufe(e, aus);
                if (aus.Beste != null) return aus;
                // Kein erlaubter Weg zu einem anderen eigenen Teil: erst
                // jetzt darf ein sonst unversorgtes Teil direkt zur Stadt.
                foreach (var g in aus.Gruppen)
                {
                    g.Ziele.Clear();
                    if (g.AnStadt || g.Ausgereizt) continue;
                    e.GesperrteZiele.TryGetValue(g.Kanten[0].Id, out var gesperrt);
                    foreach (var z in e.Ziele)
                        if (z.Stadt && gesperrt?.Contains(z.Id) != true
                            && ZielErlaubt(z.Stadt, -1, g.Teil, z.Gasse))
                            g.Ziele.Add(z);
                    g.Weg = null;
                    g.Starts.Clear();
                    g.Gerade = null;
                    g.Umweg = null;
                }
            }
            else Zielstufe(aus, true);
            WaehleStufe(e, aus);
            return aus;
        }

        private static void WaehleStufe(Versorgungseingabe e, Versorgungsauswahl aus)
        {
            aus.Beste = null;
            foreach (var g in aus.Gruppen)
            {
                PruefeAbbruch(e);
                if (g.AnStadt || g.Ausgereizt || g.Ziele.Count == 0) continue;
                g.Weg = WaehleGruppe(e, g, aus.Beste?.Laenge + 0.002f ?? e.Suchgrenze);
                if (g.Weg != null && (aus.Beste == null ||
                    g.Weg.Laenge < aus.Beste.Laenge)) aus.Beste = g.Weg;
            }
        }

        private static Versorgungsauswahl Bereite(Versorgungseingabe e)
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
                    if (ZielErlaubt(z.Stadt, zielTeil, g.Teil, z.Gasse)) g.Ziele.Add(z);
                }
            }
            return aus;
        }

        internal static bool PruefeVorplan(Versorgungseingabe e, Versorgungsvorplan vor,
            out Versorgungsauswahl aus, out string grund)
        {
            aus = Bereite(e);
            grund = "unvollstaendige Trasse";
            if (vor?.Punkte == null || vor.Punkte.Count < 2
                || vor.Stromweg == null || vor.Wasserweg == null) return false;
            grund = "andere Reihenfolge der offenen Netze";
            if (vor.OffeneTeile != aus.OffeneTeile || vor.AnderesNetzKuerzer)
                return false;
            if (BrauchtEigenesZiel(aus) && !vor.ZielEigene && !vor.EigeneVergeblich)
            {
                // Ein Stadtvorplan ist nur der Rueckfall, wenn kein eigener
                // Anschluss erreichbar ist. Hat der Vorplan das schon ohne
                // Sperren festgestellt, entfaellt die erneute volle Wahl
                // (300-670 ms am 2026-09-27); sonst wird sie hier nachgeholt.
                var entscheidung = Waehle(e);
                if (entscheidung.Beste != null && !entscheidung.Beste.Zielkante.Stadt)
                    return false;
            }
            Zielstufe(aus, !vor.ZielEigene);
            if (vor.ZielEigene && !BrauchtEigenesZiel(aus)) return false;
            Versorgungsgruppe g = null;
            foreach (var kandidat in aus.Gruppen)
                if (!kandidat.AnStadt && kandidat.Kanten.Exists(k =>
                    math.distance(k.Projektion(vor.Start).xz, vor.Start) <= 0.1f))
                {
                    if (g != null) { grund = "Startnetz nicht eindeutig"; return false; }
                    g = kandidat;
                }
            grund = "kein offenes Netz";
            if (g == null) return false;
            grund = "Netz ausgereizt";
            if (g.Ausgereizt) return false;
            Versorgungskante ziel = null;
            var mehrdeutig = false;
            foreach (var k in g.Ziele)
            {
                if (k.Stadt == !vor.ZielEigene
                    && math.distance(k.Projektion(vor.Ziel).xz, vor.Ziel) <= 0.1f)
                {
                    if (k.Id == vor.ZielId) { ziel = k; mehrdeutig = false; break; }
                    if (ziel != null) mehrdeutig = true;
                    ziel = k;
                }
            }
            if (mehrdeutig) { grund = "Zielkante nicht eindeutig"; return false; }
            grund = "Zielkante fehlt oder ist nicht erlaubt";
            if (ziel == null)
            {
                foreach (var k in e.Ziele)
                    if (k.Stadt == !vor.ZielEigene
                        && math.distance(k.Projektion(vor.Ziel).xz, vor.Ziel) <= 0.1f)
                    {
                        grund = vor.ZielEigene && g.Kanten.Contains(k)
                            ? "Ziel liegt inzwischen im Startnetz"
                            : "Ziel am Ort ist fuer dieses Teil nicht erlaubt";
                        break;
                    }
                return false;
            }
            var start = default(float3);
            var abstand = float.MaxValue;
            foreach (var k in g.Kanten)
            {
                var p = k.Projektion(vor.Start);
                var d = math.distance(p.xz, vor.Start);
                if (d < abstand) { abstand = d; start = p; }
            }
            grund = "Startpunkt liegt nicht auf offenem Versorgungsnetz";
            if (abstand > 0.1f) return false;
            var startkanten = Startkanten(g, start, out var startknoten);
            if (startkanten.Count == 0) return false;
            var ende = ziel.Projektion(vor.Ziel);
            grund = "Diagnose verlangt bestehenden Zielknoten";
            if (e.NurKnotenziele && !VersorgungsknotenDiagnose.IstEndpunkt(ziel, ende.xz)) return false;
            grund = "Zielpunkt weicht ab";
            if (math.distance(ende.xz, vor.Ziel) > 0.1f) return false;
            var punkte = new List<float2>(vor.Punkte);
            grund = "Wegenden weichen ab";
            if (math.distance(punkte[0], vor.Start) > 0.1f
                || math.distance(punkte[punkte.Count - 1], vor.Ziel) > 0.1f) return false;
            punkte[0] = start.xz;
            punkte[punkte.Count - 1] = ende.xz;
            var laenge = 0f;
            for (var i = 1; i < punkte.Count; i++) laenge += math.distance(punkte[i - 1], punkte[i]);
            grund = "Weglaenge weicht ab";
            if (!math.isfinite(laenge) || math.abs(laenge - vor.Laenge) > 0.2f) return false;
            Hindernisse(e, out var hindernisse, out var aufgeweitet);
            g.Huellen = aufgeweitet.Count;
            foreach (var h in aufgeweitet)
            { g.Ecken += h.Ring.Length; if (h.Querbar) g.QuerbareHuellen++; }
            if (vor.Hindernisweg)
            {
                var achse = VersorgungskursPruefung.Achsabstand(e.Strombreite, e.Wasserbreite);
                var zielstrassen = ziel.Stadt ? null : new HashSet<int> { ziel.Id };
                for (var i = 1; i < punkte.Count; i++)
                    if (!Versorgungsweg.Frei(punkte[i - 1], punkte[i], aufgeweitet,
                        i == 1 ? startkanten : null, e.Anschlussbereich + achse,
                        i == punkte.Count - 1 ? zielstrassen : null))
                    { grund = "Hindernisweg gesperrt"; return false; }
            }
            grund = "Tore oder Spuren gesperrt";
            if (!Wege(e, g, ziel, startkanten, hindernisse, punkte,
                out var strom, out var wasser)) return false;
            g.Weg = new Versorgungstrassenweg { Gruppe = g, Zielkante = ziel,
                Start = start, Ziel = ende, Startknoten = startknoten,
                Startkanten = new List<int>(startkanten), Punkte = punkte,
                Stromweg = strom, Wasserweg = wasser, Laenge = laenge,
                Hindernisweg = vor.Hindernisweg };
            /*
             * KEINE KONKURRENZSUCHE UEBER DIE ANDEREN NETZE.
             *
             * Welche Zone zuerst verbunden wird, hat der Vorplan entschieden.
             * Zwischen Vorplan und Bau ist unser Werkzeug aktiv, die Stadt
             * kann sich also nicht aendern; gebaut und geplant unterscheiden
             * sich nur um Zentimeter. Eine erneute Wegesuche fuer jedes
             * andere Netz kostete bei mehreren Zonen genau die Zeit, die der
             * Vorplan sparen soll (267 ms am 2026-09-27). Ob DIESE Trasse
             * gebaut werden darf, prueft alles oberhalb vollstaendig.
             */
            aus.Beste = g.Weg;
            grund = null;
            return true;
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
                startstrassen[i] = Startkanten(g, g.Starts[i], out startknoten[i]);
            IEnumerable<Versorgungsweg.Ziel> Ziele(float2 p)
            {
                for (var i = 0; i < g.Ziele.Count; i++)
                {
                    var k = g.Ziele[i];
                    if (e.NurKnotenziele)
                    {
                        foreach (var punkt in VersorgungsknotenDiagnose.Ziele(k))
                            yield return new Versorgungsweg.Ziel { Index = i, Punkt = punkt };
                        continue;
                    }
                    yield return new Versorgungsweg.Ziel { Index = i, Punkt = k.Projektion(p).xz };
                    for (var n = 0; n <= 16; n++)
                        yield return new Versorgungsweg.Ziel { Index = i,
                            Punkt = k.Position(n / 16f).xz };
                }
            }
            var achse = VersorgungskursPruefung.Achsabstand(e.Strombreite, e.Wasserbreite);
            Hindernisse(e, out var hindernisse, out var aufgeweitet);
            g.Huellen = aufgeweitet.Count;
            foreach (var h in aufgeweitet)
            { g.Ecken += h.Ring.Length; if (h.Querbar) g.QuerbareHuellen++; }
            bool Wege(List<float2> weg, int si, int zi,
                out List<float2> strom, out List<float2> wasser)
                => VersorgungstrassenPlan.Wege(e, g, g.Ziele[zi], startstrassen[si],
                    hindernisse, weg, out strom, out wasser);
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
                Hindernisweg = r == g.Umweg, Startkanten = new List<int>(),
                Punkte = new List<float2>(r.Punkte) };
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
