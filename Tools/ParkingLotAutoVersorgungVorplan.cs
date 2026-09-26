using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class VorplanKante
        {
            internal int Id;
            internal Entity Entity;
            internal Bezier4x3 Bogen;
            internal float Breite;
            internal bool Querbar;
            internal bool Versorgung;
            internal bool Muendung;
            internal bool Stadt;
            internal bool Stromtor, Wassertor;
            internal float Stromfang, Wasserfang;
            internal bool NurEnden;
        }

        private sealed class VorplanEingabe
        {
            internal readonly List<VorplanKante> Eigene = new List<VorplanKante>();
            internal readonly List<VorplanKante> Stadt = new List<VorplanKante>();
            internal readonly List<VorplanKante> Leitungen = new List<VorplanKante>();
            internal float Strombreite, Wasserbreite;
            internal float Stromfang, Wasserfang;
            internal double SchnappschussMs;
            internal int Revision;
        }

        private sealed class VorplanErgebnis
        {
            internal int Revision;
            internal Entity Zielkante;
            internal float2 Start, Ziel;
            internal float Laenge;
            internal string Grund;
            internal double SchnappschussMs, RechnungMs;
            internal bool Gefunden => Zielkante != Entity.Null;
        }

        private Task<VorplanErgebnis> _avVorplanTask;
        private CancellationTokenSource _avVorplanAbbruch;
        private VorplanErgebnis _avVorplan;
        private VorplanErgebnis _avVorplanBeimBau;
        private bool _avVorplanGemeldet;
        private int _avVorplanRevision = -1;
        private int _avVorplanUiRevision = -1;
        private long _avVorplanRuhigSeit;
        private const double VorplanRuheMs = 750;

        private static double VorplanMillis(long ticks)
            => ticks * 1000d / Stopwatch.Frequency;

        private void PflegeVorplanung()
        {
            // Waehrend der Materialisierung gehoert der fertige Vorplan noch
            // zum bestaetigten Stand. Erst am Apply wird er uebernommen.
            if (_buildStage != BuildStage.Idle) return;
            var uiRevision = _uiSystem?.Revision ?? 0;
            if (!_closed || _layoutDirty || _buildTask != null || _areaPreviewLayout == null
                || _dragPoint >= 0 || _dragEntrance >= 0
                || Mod.Optionen?.AutomatischVersorgung != true)
            {
                VerwerfeVorplanung();
                return;
            }
            if (_avVorplanRevision != _geometryRevision || _avVorplanUiRevision != uiRevision)
            {
                VerwerfeVorplanung();
                _avVorplanRevision = _geometryRevision;
                _avVorplanUiRevision = uiRevision;
                _avVorplanRuhigSeit = Stopwatch.GetTimestamp();
                return;
            }
            if (_avVorplanTask != null && _avVorplanTask.IsCompleted)
            {
                try
                {
                    var r = _avVorplanTask.GetAwaiter().GetResult();
                    if (r.Revision == _geometryRevision && !_avVorplanAbbruch.IsCancellationRequested)
                        _avVorplan = r;
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Mod.log.Error(e, "PLT-Autoversorgung Vorplanung fehlgeschlagen."); }
                _avVorplanTask = null;
            }
            if (_avVorplanTask != null || _avVorplan != null
                || VorplanMillis(Stopwatch.GetTimestamp() - _avVorplanRuhigSeit) < VorplanRuheMs) return;

            var eingabe = LeseVorplanSchnappschuss(_areaPreviewLayout, _areaPreviewSettings);
            if (eingabe == null)
            {
                _avVorplanRuhigSeit = Stopwatch.GetTimestamp();
                return;
            }
            _avVorplanAbbruch = new CancellationTokenSource();
            var token = _avVorplanAbbruch.Token;
            _avVorplanTask = Task.Run(() =>
            {
                var faden = Thread.CurrentThread;
                var vorher = faden.Priority;
                try
                {
                    faden.Priority = ThreadPriority.BelowNormal;
                    return BerechneVorplan(eingabe, token);
                }
                finally { faden.Priority = vorher; }
            }, token);
        }

        private void VerwerfeVorplanung()
        {
            _avVorplanAbbruch?.Cancel();
            _avVorplanTask = null;
            _avVorplan = null;
            _avVorplanRevision = -1;
        }

        private void MerkeVorplanungBeimBau()
        {
            if (_avVorplanTask != null && _avVorplanTask.IsCompleted)
            {
                try { _avVorplan = _avVorplanTask.GetAwaiter().GetResult(); }
                catch (Exception) { _avVorplan = null; }
            }
            _avVorplanBeimBau = _avVorplan != null && _avVorplan.Revision == _geometryRevision
                && _avVorplanUiRevision == (_uiSystem?.Revision ?? 0) ? _avVorplan : null;
            VerwerfeVorplanung();
        }

        private void MeldeVorplanung(Versorgungstrasse ist)
        {
            if (_avVorplanGemeldet) return;
            _avVorplanGemeldet = true;
            var vor = _avVorplanBeimBau;
            _avVorplanBeimBau = null;
            if (vor == null)
            {
                Mod.log.Info("PLT-Autoversorgung VORPLAN: fehlte oder veraltet; Schnappschuss - ms; Istplanung "
                    + (ist.Gefunden ? $"{ist.Laenge:F3} m, Zielkante {ist.Zielkante}." : "ohne Trasse."));
                return;
            }
            var gleich = vor.Gefunden == ist.Gefunden && (!vor.Gefunden
                || (vor.Zielkante == ist.Zielkante && math.distance(vor.Start, ist.Start.xz) <= 0.1f
                    && math.distance(vor.Ziel, ist.Ziel.xz) <= 0.1f
                    && math.abs(vor.Laenge - ist.Laenge) <= 0.1f));
            Mod.log.Info($"PLT-Autoversorgung VORPLAN: {(gleich ? "gleich" : "abweichend")}; "
                + $"Start ({vor.Start.x:F2}/{vor.Start.y:F2}) / ({ist.Start.x:F2}/{ist.Start.z:F2}), "
                + $"Ziel ({vor.Ziel.x:F2}/{vor.Ziel.y:F2}) / ({ist.Ziel.x:F2}/{ist.Ziel.z:F2}), "
                + $"Laenge {vor.Laenge:F3}/{ist.Laenge:F3} m, Zielkante {vor.Zielkante}/{ist.Zielkante}; "
                + $"Schnappschuss {vor.SchnappschussMs:F2} ms Hauptfaden, Rechnung {vor.RechnungMs:F1} ms Hintergrund; "
                + $"{vor.Grund}.");
        }

        private static Bezier4x3 VorplanGerade(float2 a, float2 b)
        {
            var start = new float3(a.x, 0, a.y);
            var ende = new float3(b.x, 0, b.y);
            return new Bezier4x3(start, math.lerp(start, ende, 1f / 3),
                math.lerp(start, ende, 2f / 3), ende);
        }

        private VorplanEingabe LeseVorplanSchnappschuss(ParkingLayout layout, LayoutSettings settings)
        {
            var uhr = Stopwatch.GetTimestamp();
            var r = new VorplanEingabe { Revision = _geometryRevision };
            if (layout?.NetLine == null || settings == null || _netSearchSystem == null) return null;
            var strom = FindeVersorgungsprefab(true);
            var wasser = FindeVersorgungsprefab(false);
            if (strom == Entity.Null || wasser == Entity.Null) return null;
            r.Strombreite = EntityManager.GetComponentData<NetGeometryData>(strom).m_DefaultWidth;
            r.Wasserbreite = EntityManager.GetComponentData<NetGeometryData>(wasser).m_DefaultWidth;
            r.Stromfang = EntityManager.GetComponentData<LocalConnectData>(strom).m_SearchDistance;
            r.Wasserfang = EntityManager.GetComponentData<LocalConnectData>(wasser).m_SearchDistance;
            var min = new float2(float.MaxValue); var max = new float2(float.MinValue);
            foreach (var stueck in layout.NetLine)
            {
                var zoning = stueck.Kind == "zoning";
                var gasse = stueck.Kind == "entrance" && Zufahrtsarten.IstGasse(stueck.Art);
                var breite = zoning ? (float)ParkingGeometry.ZoningStrassenbreite
                    : gasse ? (float)new Entrance { Art = stueck.Art }.Breite(settings.Ai, settings.Gassenbreite)
                    : stueck.Kind == "cross" ? (float)settings.Cw : (float)settings.Ai;
                r.Eigene.Add(new VorplanKante { Id = -r.Eigene.Count - 1,
                    Bogen = VorplanGerade(stueck.A, stueck.B), Breite = breite,
                    Versorgung = zoning || gasse, Muendung = gasse,
                    Querbar = !zoning && !gasse,
                    Stromtor = zoning || gasse, Wassertor = zoning || gasse,
                    Stromfang = r.Stromfang, Wasserfang = r.Wasserfang });
                min = math.min(min, math.min(stueck.A, stueck.B));
                max = math.max(max, math.max(stueck.A, stueck.B));
            }
            if (r.Eigene.Count == 0) return null;
            var baum = _netSearchSystem.GetNetSearchTree(true, out var deps);
            deps.Complete();
            using var gefunden = new NativeList<Entity>(64, Allocator.Temp);
            var iterator = new EntityIterator { Bounds = new Bounds2(min - AutoVersorgungSuchradius,
                max + AutoVersorgungSuchradius), Results = gefunden };
            baum.Iterate(ref iterator);
            var gesehen = new HashSet<Entity>();
            foreach (var e in gefunden)
            {
                if (!gesehen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                    || EntityManager.HasComponent<Temp>(e) || !EntityManager.HasComponent<Edge>(e)
                    || !EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!EntityManager.HasComponent<NetGeometryData>(prefab)) continue;
                var road = EntityManager.HasComponent<RoadData>(prefab);
                var kabel = EntityManager.HasComponent<ElectricityConnectionData>(prefab)
                    || EntityManager.HasComponent<WaterPipeConnectionData>(prefab);
                if (!road && !kabel) continue;
                var k = new VorplanKante { Id = e.Index, Entity = e,
                    Bogen = EntityManager.GetComponentData<Curve>(e).m_Bezier,
                    Breite = EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth };
                if (road && !EntityManager.HasComponent<Owner>(e) && KanteNimmtVersorgung(e))
                {
                    k.Stadt = true;
                    k.NurEnden = (EntityManager.GetComponentData<NetGeometryData>(prefab).m_Flags
                        & GeometryFlags.NoEdgeConnection) != 0;
                    k.Stromtor = AvAnschlussLayer(prefab, strom, out _, out _);
                    k.Wassertor = AvAnschlussLayer(prefab, wasser, out _, out _);
                    k.Stromfang = r.Stromfang; k.Wasserfang = r.Wasserfang;
                    r.Stadt.Add(k);
                }
                else if (!road && kabel) r.Leitungen.Add(k);
            }
            r.SchnappschussMs = VorplanMillis(Stopwatch.GetTimestamp() - uhr);
            return r;
        }

        private static VorplanErgebnis BerechneVorplan(VorplanEingabe e, CancellationToken token)
        {
            var uhr = Stopwatch.GetTimestamp();
            var r = new VorplanErgebnis { Revision = e.Revision,
                SchnappschussMs = e.SchnappschussMs, Grund = "kein zulaessiger Weg" };
            if (e.Stadt.Count == 0 || e.Eigene.Count == 0)
            { r.Grund = "keine Stadtstrasse oder eigene Kante"; return r; }
            var versorgbar = e.Eigene.FindAll(k => k.Versorgung);
            if (versorgbar.Count == 0)
            { r.Grund = "kein versorgungsfaehiges Layout-Stueck"; return r; }
            // Nur gemeinsame Layout-Endpunkte bilden einen Knoten. Die
            // Gassenmuendung verbindet genau ihre Gruppe mit der Stadt.
            var offen = new HashSet<VorplanKante>(versorgbar);
            var offeneKanten = new List<VorplanKante>();
            while (offen.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                VorplanKante erste = null;
                foreach (var k in offen) { erste = k; break; }
                offen.Remove(erste);
                var gruppe = new List<VorplanKante> { erste };
                for (var i = 0; i < gruppe.Count; i++)
                {
                    var nachbarn = new List<VorplanKante>();
                    foreach (var k in offen)
                        if (VorplanVerbunden(gruppe[i], k)) nachbarn.Add(k);
                    foreach (var k in nachbarn) { offen.Remove(k); gruppe.Add(k); }
                }
                var anStadt = false;
                foreach (var k in gruppe)
                    if (k.Muendung)
                        foreach (var stadt in e.Stadt)
                            if (VorplanAbstand(stadt, k.Bogen.a.xz) <= stadt.Breite / 2 + 0.1f)
                                anStadt = true;
                if (!anStadt) offeneKanten.AddRange(gruppe);
            }
            if (offeneKanten.Count == 0)
            { r.Grund = "alle Layout-Gruppen per Knoten an Stadtstrasse"; return r; }
            var achse = VersorgungskursPruefung.Achsabstand(e.Strombreite, e.Wasserbreite);
            var hindernisse = new List<Versorgungsweg.Hindernis>();
            void Huelle(VorplanKante k, float zugabe)
                => Versorgungsweg.Bogen(hindernisse, k.Id, k.Bogen.a.xz, k.Bogen.b.xz,
                    k.Bogen.c.xz, k.Bogen.d.xz, k.Breite / 2 + AutoVersorgungSicherheitszugabe
                    + zugabe, querbar: k.Querbar);
            var zugabe = math.max(e.Strombreite, e.Wasserbreite) / 2 + achse;
            foreach (var k in e.Eigene) { token.ThrowIfCancellationRequested(); Huelle(k, zugabe); }
            foreach (var k in e.Leitungen) { token.ThrowIfCancellationRequested(); Huelle(k, zugabe); }
            var starts = new List<float2>(); var startKanten = new List<HashSet<int>>();
            void Merke(VorplanKante k, float2 p)
            {
                MathUtils.Distance(k.Bogen.xz, p, out var t);
                p = MathUtils.Position(k.Bogen, t).xz;
                foreach (var alt in starts) if (math.distance(alt, p) < 0.01f) return;
                starts.Add(p);
                var kanten = new HashSet<int>();
                foreach (var verbunden in versorgbar)
                    if (VorplanAbstand(verbunden, p) <= 0.1f) kanten.Add(verbunden.Id);
                startKanten.Add(kanten);
            }
            foreach (var k in offeneKanten)
            {
                token.ThrowIfCancellationRequested();
                Merke(k, MathUtils.Position(k.Bogen, 0.5f).xz);
                foreach (var ziel in e.Stadt)
                {
                    token.ThrowIfCancellationRequested();
                    float2 Projektion(Bezier4x3 b, float2 p)
                    {
                        MathUtils.Distance(b.xz, p, out var t);
                        return MathUtils.Position(b, t).xz;
                    }
                    foreach (var p in Versorgungsnetz.Kantenpunkte(
                        t => MathUtils.Position(k.Bogen, t).xz,
                        p => Projektion(k.Bogen, p),
                        t => MathUtils.Position(ziel.Bogen, t).xz,
                        p => Projektion(ziel.Bogen, p))) Merke(k, p);
                }
            }
            foreach (var k in offeneKanten)
            {
                Merke(k, k.Bogen.a.xz);
                Merke(k, k.Bogen.d.xz);
            }
            bool Tor(VorplanKante k, float2 p, bool istStrom)
                => (istStrom ? k.Stromtor : k.Wassertor)
                    && VersorgungskursPruefung.Anschluss(VorplanAbstand(k, p), k.Breite,
                        istStrom ? e.Strombreite : e.Wasserbreite,
                        istStrom ? k.Stromfang : k.Wasserfang, true);
            bool Zulassen(List<float2> weg, int z)
            {
                var start = startKanten[starts.IndexOf(weg[0])];
                return Versorgungsweg.Spuren(weg, e.Strombreite, e.Wasserbreite, hindernisse,
                    start, AutoVersorgungAnschlussbereich, out _, out _,
                    (p, s) => Tor(e.Stadt[z], p, s), null,
                    (p, s) => versorgbar.Exists(k => start.Contains(k.Id) && Tor(k, p, s)));
            }
            IEnumerable<Versorgungsweg.Ziel> Ziele(float2 p)
            {
                for (var i = 0; i < e.Stadt.Count; i++)
                {
                    var k = e.Stadt[i];
                    MathUtils.Distance(k.Bogen.xz, p, out var t);
                    yield return new Versorgungsweg.Ziel { Index = i,
                        Punkt = MathUtils.Position(k.Bogen, k.NurEnden ? (t < 0.5f ? 0 : 1) : t).xz };
                    for (var n = 0; n <= 16; n++)
                        yield return new Versorgungsweg.Ziel { Index = i,
                            Punkt = MathUtils.Position(k.Bogen, n / 16f).xz };
                }
            }
            token.ThrowIfCancellationRequested();
            var gerade = Versorgungsnetz.Gerade(starts, Ziele,
                (i, z) => Zulassen(new List<float2> { starts[i], z.Punkt }, z.Index),
                float.MaxValue, () => token.IsCancellationRequested);
            token.ThrowIfCancellationRequested();
            var umweg = Versorgungsweg.Suche(starts, hindernisse, i => startKanten[i], Ziele,
                Zulassen, AutoVersorgungAnschlussbereich + achse, null,
                gerade.Punkte == null ? float.MaxValue : gerade.Laenge + 0.002f,
                () => token.IsCancellationRequested);
            token.ThrowIfCancellationRequested();
            var wahl = umweg.Punkte != null && (gerade.Punkte == null
                || Versorgungsnetz.Kuerzer(umweg.Laenge, gerade.Laenge)) ? umweg : gerade;
            if (wahl.Punkte != null)
            {
                r.Start = wahl.Punkte[0]; r.Ziel = wahl.Punkte[wahl.Punkte.Count - 1];
                r.Zielkante = e.Stadt[wahl.Ziel].Entity; r.Laenge = wahl.Laenge;
                r.Grund = "Layoutachsen und Welt-Schnappschuss";
            }
            r.RechnungMs = VorplanMillis(Stopwatch.GetTimestamp() - uhr);
            return r;
        }

        private static float VorplanAbstand(VorplanKante k, float2 p)
        {
            var abstand = MathUtils.Distance(k.Bogen.xz, p, out var t);
            if (k.NurEnden)
                abstand = math.min(math.distance(k.Bogen.a.xz, p), math.distance(k.Bogen.d.xz, p));
            return abstand;
        }

        private static bool VorplanVerbunden(VorplanKante a, VorplanKante b)
            => math.distance(a.Bogen.a.xz, b.Bogen.a.xz) <= 0.05f
                || math.distance(a.Bogen.a.xz, b.Bogen.d.xz) <= 0.05f
                || math.distance(a.Bogen.d.xz, b.Bogen.a.xz) <= 0.05f
                || math.distance(a.Bogen.d.xz, b.Bogen.d.xz) <= 0.05f;
    }
}
