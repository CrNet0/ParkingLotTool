using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private float _avStrombreite, _avWasserbreite;
        private Entity _avStromprefab, _avWasserprefab;

        private List<Versorgungstrasse> WaehleVersorgungstrassen(Entity traeger)
        {
            _avLetzteIstEingabe = null;
            var trassen = new List<Versorgungstrasse>();
            var eigene = SammleUnsereKanten(traeger);
            if (eigene.Count == 0)
            {
                Mod.log.Warn("PLT-Autoversorgung: der Traeger hat keine eigene Strassenkanten - kein Startpunkt bestimmbar.");
                return trassen;
            }
            var zielstrassen = SammleZielstrassen(eigene);
            if (!zielstrassen.Exists(z => !EntityManager.HasComponent<Owner>(z.Kante)))
            {
                _avNochOffeneNetze = 0;
                Mod.log.Info("PLT-Autoversorgung: keine Stadtstrasse im Suchfeld; keine Leitungen angelegt.");
                return trassen;
            }
            var e = AvLeseIstEingabe(traeger, zielstrassen, _avAlleEigenen);
            Versorgungsauswahl aus;
            var vor = _avVorplanBeimBau;
            var nummer = _avVorplanIndex + 1;
            if (vor?.Gefunden == true && _avVorplanIndex < vor.Trassen.Count)
            {
                var uhr = Stopwatch.StartNew();
                var trasse = vor.Trassen[_avVorplanIndex];
                if (VersorgungstrassenPlan.PruefeVorplan(e, trasse, out aus, out var grund))
                {
                    Mod.log.Info($"PLT-Autoversorgung VORPLAN: verwendet (Trasse {nummer}/{vor.Trassen.Count}); Pruefung Hauptfaden "
                        + $"{uhr.Elapsed.TotalMilliseconds:F2} ms.");
                    _avVorplanIndex++;
                }
                else
                {
                    Mod.log.Info($"PLT-Autoversorgung VORPLAN: verworfen ({grund}, Trasse {nummer}/{vor.Trassen.Count}); "
                        + $"Pruefung Hauptfaden {uhr.Elapsed.TotalMilliseconds:F2} ms.");
                    VerwerfeRestvorplan(grund);
                    aus = VersorgungstrassenPlan.Waehle(e);
                }
            }
            else
            {
                // Nur ein echter Rueckfall heisst "verworfen". Ohne Trasse oder
                // nach der letzten verwendeten Trasse bestaetigt die volle Wahl
                // nur, dass nichts mehr zu tun ist.
                Mod.log.Info(_avVorplanRueckfallGrund != null
                    ? $"PLT-Autoversorgung VORPLAN: verworfen ({_avVorplanRueckfallGrund}); volle Wahl."
                    : vor == null ? $"PLT-Autoversorgung VORPLAN: fehlte oder veraltet ({_avVorplanGrund}); volle Wahl."
                    : vor.Gefunden ? "PLT-Autoversorgung VORPLAN: alle Trassen verwendet; volle Wahl zur Bestaetigung."
                    : "PLT-Autoversorgung VORPLAN: ohne Trasse; volle Wahl zur Bestaetigung.");
                aus = VersorgungstrassenPlan.Waehle(e);
            }
            _avLetzteIstEingabe = aus;
            var gemesseneHuellen = aus.Beste?.Gruppe;
            if (gemesseneHuellen == null)
                foreach (var gruppe in aus.Gruppen)
                    if (gruppe.Huellen > 0) { gemesseneHuellen = gruppe; break; }
            Mod.log.Info($"PLT-Autoversorgung QUERUNGSREGEL: "
                + $"{e.Hinderniskanten.FindAll(k => k.Querbar).Count} eigene Fahrgassen-/Pfadkanten querbar, "
                + $"{(gemesseneHuellen?.Huellen ?? 0) - (gemesseneHuellen?.QuerbareHuellen ?? 0)} gesperrte Huellen davon "
                + $"{e.Leitungen.Count} fremde Erdleitung(en); "
                + "Querwinkel mindestens 45 Grad, Laengsfahrt bleibt gesperrt.");
            Mod.log.Info($"PLT-Autoversorgung: {aus.OhneVersorgung} Fahrgassen-/Pfadkanten ohne Versorgungsbedarf; "
                + $"{e.Eigene.Count - aus.OhneVersorgung} versorgungsfaehige Kanten. Pfade verbinden keine Versorgungsgruppen.");
            Mod.log.Info($"PLT-Autoversorgung: {e.Eigene.Count} eigene Kante(n) "
                + $"bilden {aus.Gruppen.Count} getrennte(s) Netz(e); Anschlussbedarf wird je Netz geprueft.");
            Mod.log.Info($"PLT-Autoversorgung TEILE: {aus.Gruppen.Count} eigene(s) Netz(e), "
                + $"{aus.OffeneTeile} davon noch nicht an der Stadt, {aus.PerKnotenAnStadt} per Knoten an einer Stadtstrasse. "
                + "Zuerst werden erreichbare eigene Teile verbunden; danach folgt bei Bedarf "
                + "ein Stadtanschluss. Ohne eigenen Weg ist die Stadt der Rueckfall.");
            var entitaeten = new Dictionary<int, Entity>();
            foreach (var entity in _avAlleEigenen) entitaeten[entity.Index] = entity;
            foreach (var z in zielstrassen) entitaeten[z.Kante.Index] = z.Kante;
            for (var i = 0; i < aus.Gruppen.Count; i++)
            {
                var g = aus.Gruppen[i];
                var name = $"Netz {i + 1}/{aus.Gruppen.Count}";
                var gesperrteStarts = g.Kanten.FindAll(k => k.AnschlussGesperrt).Count;
                if (gesperrteStarts > 0)
                    Mod.log.Warn($"PLT-VERSORGUNG-DIAG OHNE-ZONINGSTART {name}: "
                        + $"{gesperrteStarts}/{g.Kanten.Count} Kanten als Start/Ziel gesperrt; "
                        + $"Stadtpfad={(g.AnStadt ? 1 : 0)}, Trasse={(g.Weg != null ? 1 : 0)}. "
                        + (!g.AnStadt && g.Weg == null ? "Dieses Netz bleibt ohne neue Leitung." : "Gruppe bleibt in der Bedarfspruefung."));
                if (g.AnStadt)
                {
                    Mod.log.Info($"PLT-Autoversorgung {name}: haengt am Stadtnetz - nichts zu tun.");
                    continue;
                }
                if (g.Ausgereizt)
                {
                    var stand = AvStandFuer(g.Kanten.ConvertAll(k => entitaeten[k.Id]), false);
                    Mod.log.Warn($"PLT-Autoversorgung {name}: {stand?.Versuche ?? 0} Anlaeufe ueber "
                        + $"{stand?.GesperrteZiele.Count ?? 0} verschiedene Ziele, alle abgewiesen. "
                        + "Dieses Netz bleibt ohne Leitung - beim naechsten Oeffnen des Werkzeugs wird es erneut versucht.");
                    continue;
                }
                if (g.Gesperrt > 0)
                    Mod.log.Info($"PLT-Autoversorgung {name}: {g.Ziele.Count} Ziele uebrig, "
                        + $"{g.Gesperrt} aus frueheren Anlaeufen gesperrt.");
                if (g.Ziele.Count == 0)
                    Mod.log.Warn($"PLT-Autoversorgung {name}: {zielstrassen.Count} Kante(n) im Suchfeld, "
                        + "aber keine davon ist ein erlaubtes Ziel - entweder liegt keine fremde Strasse "
                        + "in Reichweite, oder alle gefundenen gehoeren schon zu diesem Teil. "
                        + "Dieses Netz bleibt ohne Leitung; es wird nicht nach einem Weg gesucht.");
                if (g.Gerade?.Punkte != null)
                {
                    var z = g.Ziele[g.Gerade.Ziel];
                    Mod.log.Info($"PLT-Autoversorgung GERADE [{name}: Kante/Knoten]: {g.Starts.Count} Starts, "
                        + $"{g.Gerade.Zielpruefungen} Kombinationen, kuerzeste zulaessige Gerade {g.Gerade.Laenge:F3} m; "
                        + $"Start ({g.Starts[g.Gerade.Start].x:F2}/{g.Starts[g.Gerade.Start].z:F2}), "
                        + $"Ziel {entitaeten[z.Id]}, eigene Zielstrasse {(z.Stadt ? 0 : 1)}. Graphwege werden noch verglichen.");
                }
                if (g.Umweg != null)
                {
                    if (g.Ecken > 800)
                        Mod.log.Warn($"PLT-Autoversorgung {name}: {g.Huellen} Huellen mit {g.Ecken} Ecken - "
                            + "die Wegesuche darauf ist quadratisch und kann mehrere Sekunden dauern.");
                    Mod.log.Info($"PLT-Autoversorgung HINDERNISWEG [{name}]: {g.Starts.Count} Starts, "
                        + $"{g.Ziele.Count} Zielstrassen, {g.Huellen} aufgeweitete Huellen, "
                        + $"{g.Umweg.Erreicht}/{g.Umweg.Knoten} Graphknoten erreicht, "
                        + $"{g.Umweg.Sichtpruefungen} Sichtpruefungen, {g.Umweg.Zielpruefungen} Zielpruefungen, "
                        + $"{(g.Umweg.Punkte == null ? 0 : g.Umweg.Punkte.Count - 1)} Teilstrecken, "
                        + $"{g.UmwegMs:F1} ms, Suchgrenze {g.Suchgrenze:F3} m. "
                        + (g.Umweg.Punkte == null ? "Keine zulaessige Verbesserung innerhalb der Suchgrenze; keine Strassenquerung freigegeben."
                            : $"Gewaehlter Graphweg {g.Umweg.Laenge:F2} m."));
                }
                // Wie frueher getrennt: "nicht kuerzer als ein anderes Netz" ist
                // kein Fehlschlag - dieses Netz kommt beim naechsten Anlauf dran.
                if (g.Weg == null && g.Ziele.Count > 0 && g.Suchgrenze < float.MaxValue)
                    Mod.log.Info($"PLT-Autoversorgung {name}: keine bessere Trasse innerhalb "
                        + $"{g.Suchgrenze:F3} m; Netz bleibt fuer den naechsten Anschluss offen.");
                else if (g.Weg == null && g.Ziele.Count > 0)
                    Mod.log.Warn($"PLT-Autoversorgung {name}: {g.Starts.Count} Starts, "
                        + $"{g.Ziele.Count} erlaubte Ziele, 0 zulaessige Trassen. Dieses Netz bleibt ohne Leitung.");
            }
            _avNochOffeneNetze = 0;
            foreach (var g in aus.Gruppen) if (!g.AnStadt && !g.Ausgereizt) _avNochOffeneNetze++;
            if (aus.Beste != null)
            {
                var w = aus.Beste;
                var gruppe = w.Gruppe.Kanten.ConvertAll(k => entitaeten[k.Id]);
                var startkanten = w.Startkanten.ConvertAll(id => entitaeten[id]);
                var knoten = Entity.Null;
                if (w.Startknoten != 0)
                    foreach (var entity in gruppe)
                    {
                        var edge = EntityManager.GetComponentData<Edge>(entity);
                        if (edge.m_Start != Entity.Null && edge.m_Start.Index + 1 == w.Startknoten)
                        { knoten = edge.m_Start; break; }
                        if (edge.m_End != Entity.Null && edge.m_End.Index + 1 == w.Startknoten)
                        { knoten = edge.m_End; break; }
                    }
                var name = $"Netz {aus.Gruppen.IndexOf(w.Gruppe) + 1}/{aus.Gruppen.Count}";
                var t = new Versorgungstrasse { Start = w.Start, Ziel = w.Ziel,
                    Zielkante = entitaeten[w.Zielkante.Id], Startnetz = gruppe,
                    Startkanten = startkanten, Startknoten = knoten,
                    Stromweg = w.Stromweg, Wasserweg = w.Wasserweg,
                    Laenge = w.Laenge, Herkunft = name + (w.Hindernisweg ? ": Hindernisweg" : ": Kante/Knoten") };
                trassen.Add(t);
                _avNochOffeneNetze--;
                Mod.log.Info($"PLT-Autoversorgung AUSWAHL: {t.Herkunft}, {t.Laenge:F3} m, "
                    + $"Start ({t.Start.x:F2}/{t.Start.z:F2}), Ziel {t.Zielkante} ({t.Ziel.x:F2}/{t.Ziel.z:F2}), "
                    + $"{(t.Startknoten == Entity.Null ? 1 : 0)} Kantenstart; {_avNochOffeneNetze} weitere offene Netze.");
            }
            else AvMesseNetzabdeckung();
            return trassen;
        }

        private Versorgungsauswahl _avLetzteIstEingabe;

        private Versorgungskante AvLeseKante(Entity e, Entity strom, Entity wasser)
        {
            if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Curve>(e)
                || !EntityManager.HasComponent<PrefabRef>(e)
                || !EntityManager.HasComponent<Edge>(e)) return null;
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            if (!EntityManager.HasComponent<NetGeometryData>(prefab)) return null;
            var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
            var edge = EntityManager.GetComponentData<Edge>(e);
            var geo = EntityManager.GetComponentData<NetGeometryData>(prefab);
            var fahrgasse = EntityManager.HasComponent<NetData>(prefab)
                && (EntityManager.GetComponentData<NetData>(prefab).m_LocalConnectLayers
                    & (Layer.PowerlineLow | Layer.WaterPipe | Layer.SewagePipe)) == 0;
            var r = new Versorgungskante { Id = e.Index,
                Startknoten = edge.m_Start == Entity.Null ? 0 : edge.m_Start.Index + 1,
                Endknoten = edge.m_End == Entity.Null ? 0 : edge.m_End.Index + 1,
                Startpunkt = b.a, Endpunkt = b.d,
                SteuerungB = b.b.xz, SteuerungC = b.c.xz,
                Position = t => MathUtils.Position(b, t),
                Projektion = p => {
                    MathUtils.Distance(b.xz, p, out var t);
                    return MathUtils.Position(b, t);
                },
                Breite = geo.m_DefaultWidth, NurEnden = (geo.m_Flags & GeometryFlags.NoEdgeConnection) != 0,
                Versorgung = KanteNimmtVersorgung(e), Querbar = fahrgasse,
                Stadt = !EntityManager.HasComponent<Owner>(e),
                Gasse = GassenPrefab.Ist(_prefabSystem, prefab),
                AnschlussGesperrt = AvZoninganschlussGesperrt(prefab),
                Stromfang = EntityManager.GetComponentData<LocalConnectData>(strom).m_SearchDistance,
                Wasserfang = EntityManager.GetComponentData<LocalConnectData>(wasser).m_SearchDistance,
                Stromtor = EntityManager.HasComponent<NetData>(prefab)
                    && AvAnschlussLayer(prefab, strom, out _, out _),
                Wassertor = EntityManager.HasComponent<NetData>(prefab)
                    && AvAnschlussLayer(prefab, wasser, out _, out _) };
            if (edge.m_Start != Entity.Null && EntityManager.HasComponent<Node>(edge.m_Start))
                r.Startpunkt = EntityManager.GetComponentData<Node>(edge.m_Start).m_Position;
            if (edge.m_End != Entity.Null && EntityManager.HasComponent<Node>(edge.m_End))
                r.Endpunkt = EntityManager.GetComponentData<Node>(edge.m_End).m_Position;
            return r;
        }

        private bool AvZoninganschlussGesperrt(Entity prefab)
            => Mod.Aus("versorgung-ohne-zoningstart")
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var asset)
                && asset.name.StartsWith("PLT Zoningstrasse (", System.StringComparison.Ordinal);

        private Versorgungseingabe AvLeseIstEingabe(Entity traeger,
            List<(Entity Kante, Bezier4x3 Bogen)> zielstrassen,
            List<Entity> unsere)
        {
            var e = new Versorgungseingabe { Strombreite = _avStrombreite,
                Wasserbreite = _avWasserbreite,
                Sicherheitszugabe = AutoVersorgungSicherheitszugabe,
                Anschlussbereich = AutoVersorgungAnschlussbereich,
                NurKnotenziele = Mod.Aus("versorgung-kantenziel") };
            var kanten = new Dictionary<int, Versorgungskante>();
            Versorgungskante Hole(Entity entity)
            {
                if (!kanten.TryGetValue(entity.Index, out var k))
                    kanten[entity.Index] = k = AvLeseKante(entity, _avStromprefab, _avWasserprefab);
                return k;
            }
            foreach (var entity in unsere)
            {
                var k = Hole(entity);
                if (k != null) e.Hinderniskanten.Add(k);
            }
            foreach (var entity in SammleUnsereKanten(traeger))
            {
                var k = Hole(entity);
                if (k == null) continue;
                k.FlussAnStadt = AvZielHatStadtpfad(entity);
                var edge = EntityManager.GetComponentData<Edge>(entity);
                k.KnotenAnStadt = KnotenHatStadtstrasse(edge.m_Start)
                    || KnotenHatStadtstrasse(edge.m_End);
                e.Eigene.Add(k);
            }
            foreach (var z in zielstrassen)
            {
                var k = Hole(z.Kante);
                if (k != null) e.Ziele.Add(k);
            }
            foreach (var entity in _avFremdleitungen)
            {
                var k = Hole(entity);
                if (k != null) { k.Querbar = false; e.Leitungen.Add(k); }
            }
            foreach (var v in _avVerbindungen)
                e.Verbindungen.Add((v.Start.xz, v.Ziel.xz, v.ZielIstStadt));
            foreach (var gruppe in SammleVersorgungsgruppen(
                SammleUnsereKanten(traeger).FindAll(KanteNimmtVersorgung)))
            {
                var stand = AvStandFuer(gruppe, false);
                if (stand == null) continue;
                foreach (var entity in gruppe)
                {
                    if (stand.Versuche >= AutoVersorgungHoechstversuche)
                        e.Ausgereizt.Add(entity.Index);
                    var gesperrt = new HashSet<int>();
                    foreach (var ziel in stand.GesperrteZiele) gesperrt.Add(ziel.Index);
                    e.GesperrteZiele[entity.Index] = gesperrt;
                }
            }
            return e;
        }
    }
}
