using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
        private static Bezier4x3 VorplanGerade(float2 a, float2 b)
        {
            var start = new float3(a.x, 0, a.y);
            var ende = new float3(b.x, 0, b.y);
            return new Bezier4x3(start, math.lerp(start, ende, 1f / 3),
                math.lerp(start, ende, 2f / 3), ende);
        }

        private static void VorplanSetzeBogen(Versorgungskante k, Bezier4x3 b)
        {
            k.Startpunkt = b.a; k.Endpunkt = b.d;
            k.SteuerungB = b.b.xz; k.SteuerungC = b.c.xz;
            k.Position = t => MathUtils.Position(b, t);
            k.Projektion = p => { MathUtils.Distance(b.xz, p, out var t);
                return MathUtils.Position(b, t); };
        }

        private Versorgungskante VorplanLayoutKante(int id, Bezier4x3 b,
            Entity prefab, Entity strom, Entity wasser)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetGeometryData>(prefab)
                || !EntityManager.HasComponent<NetData>(prefab)) return null;
            var geo = EntityManager.GetComponentData<NetGeometryData>(prefab);
            var versorgung = AvPrefabNimmtVersorgung(prefab);
            return new Versorgungskante { Id = id, Startpunkt = b.a, Endpunkt = b.d,
                SteuerungB = b.b.xz, SteuerungC = b.c.xz,
                Position = t => MathUtils.Position(b, t),
                Projektion = p => { MathUtils.Distance(b.xz, p, out var t);
                    return MathUtils.Position(b, t); },
                Breite = geo.m_DefaultWidth, Versorgung = versorgung,
                Querbar = !versorgung, NurEnden = (geo.m_Flags & GeometryFlags.NoEdgeConnection) != 0,
                Stromfang = EntityManager.GetComponentData<LocalConnectData>(strom).m_SearchDistance,
                Wasserfang = EntityManager.GetComponentData<LocalConnectData>(wasser).m_SearchDistance,
                Stromtor = AvAnschlussLayer(prefab, strom, out _, out _),
                Wassertor = AvAnschlussLayer(prefab, wasser, out _, out _) };
        }

        private VorplanEingabe LeseVorplanSchnappschuss(ParkingLayout layout, LayoutSettings settings)
        {
            var uhr = Stopwatch.GetTimestamp();
            if (layout?.NetLine == null || settings == null || _netSearchSystem == null) return null;
            // Dieselbe Entscheidung wie beim Abriss, auch fuer erhaltene
            // Zoningnetze - aber ohne den Bauzustand zu setzen.
            var erhalten = new HashSet<Entity>();
            if (IsEditing) SammleErhalteneZoningteile(erhalten, false);
            var strom = FindeVersorgungsprefab(true);
            var wasser = FindeVersorgungsprefab(false);
            if (strom == Entity.Null || wasser == Entity.Null) return null;
            if (!TryChooseDrivablePath(settings.Ai, out var breit, out _)
                || !TryChooseDrivablePath(settings.Cw, out var schmal, out _)) return null;
            var r = new VorplanEingabe { Revision = _geometryRevision };
            var e = r.Plan;
            e.NurKnotenziele = Mod.Aus("versorgung-kantenziel");
            e.Strombreite = EntityManager.GetComponentData<NetGeometryData>(strom).m_DefaultWidth;
            e.Wasserbreite = EntityManager.GetComponentData<NetGeometryData>(wasser).m_DefaultWidth;
            e.Sicherheitszugabe = AutoVersorgungSicherheitszugabe;
            e.Anschlussbereich = AutoVersorgungAnschlussbereich;
            var zoning = Entity.Null;
            foreach (var stueck in layout.NetLine)
                if (stueck.Kind == "zoning")
                { TryResolveZoningRoad(settings.Zoningstrasse, out zoning); break; }
            var min = new float2(float.MaxValue); var max = new float2(float.MinValue);
            Versorgungskante FuegeHinzu(float2 a, float2 b, Entity prefab)
            {
                var id = -e.Eigene.Count - 1;
                var k = VorplanLayoutKante(id, VorplanGerade(a, b), prefab, strom, wasser);
                if (k == null) return null;
                e.Eigene.Add(k);
                e.Hinderniskanten.Add(k);
                if (k.Versorgung) e.Ziele.Add(k);
                min = math.min(min, math.min(a, b));
                max = math.max(max, math.max(a, b));
                return k;
            }
            foreach (var stueck in layout.NetLine)
            {
                var gasse = stueck.Kind == "entrance" && Zufahrtsarten.IstGasse(stueck.Art);
                var von = stueck.A; var nach = stueck.B;
                Entity prefab;
                if (gasse)
                {
                    if (!TryResolveZufahrtsgasse(stueck.Art, out prefab)) continue;
                }
                else if (stueck.Kind == "entrance" && stueck.Art == Zufahrtsart.Fussweg)
                {
                    if (TryResolvePedestrianPath(out prefab, gesetzterZugang: true))
                        FuegeHinzu(von, nach, prefab);
                    continue;
                }
                else if (stueck.Kind == "entrance" && (stueck.Art == Zufahrtsart.Einfahrt
                    || stueck.Art == Zufahrtsart.Ausfahrt))
                {
                    if (!TryResolvePathPrefab(OnewayPathName, out prefab)) continue;
                    if (Zufahrtsarten.FaehrtHinaus(stueck.Art)) { von = stueck.B; nach = stueck.A; }
                    FuegeHinzu(von, nach, prefab);
                    if (TryResolvePedestrianPath(out var fuss))
                    {
                        var v = nach - von;
                        var laenge = math.length(v);
                        if (laenge >= 1f)
                        {
                            var normal = new float2(-v.y, v.x) / laenge;
                            var versatz = normal * ((OnewayPathWidth - PedestrianPathWidth) / 2f);
                            FuegeHinzu(von - versatz, nach - versatz, fuss);
                            FuegeHinzu(von + versatz, nach + versatz, fuss);
                        }
                    }
                    continue;
                }
                else prefab = stueck.Kind == "zoning" ? zoning
                    : stueck.Kind == "cross" ? schmal : breit;
                var eigene = FuegeHinzu(von, nach, prefab);
                if (eigene != null && stueck.Kind == "zoning")
                    eigene.AnschlussGesperrt = Mod.Aus("versorgung-ohne-zoningstart");
                if (gasse && eigene != null)
                {
                    eigene.Gasse = true;
                    r.Gassen.Add((eigene, stueck.A, stueck.B,
                        Zufahrtsarten.FaehrtHinaus(stueck.Art)));
                }
            }
            if (e.Eigene.Count == 0) return null;
            var baum = _netSearchSystem.GetNetSearchTree(true, out var deps);
            deps.Complete();
            using var gefunden = new NativeList<Entity>(64, Allocator.Temp);
            var iterator = new EntityIterator { Bounds = new Bounds2(min - AutoVersorgungSuchradius,
                max + AutoVersorgungSuchradius), Results = gefunden };
            baum.Iterate(ref iterator);
            var gesehen = new HashSet<Entity>();
            foreach (var entity in gefunden)
            {
                if (!gesehen.Add(entity) || !EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Deleted>(entity) || EntityManager.HasComponent<Temp>(entity)
                    || !EntityManager.HasComponent<Edge>(entity)
                    || !EntityManager.HasComponent<Curve>(entity)
                    || !EntityManager.HasComponent<PrefabRef>(entity)) continue;
                // Beim Edit werden die Netze des alten Lots vor dem Neubau
                // geloescht. Sie duerfen weder Ziel noch Hindernis sein.
                if (IsEditing && !erhalten.Contains(entity)
                    && VorplanGehoertZumEditLot(entity)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                var road = EntityManager.HasComponent<RoadData>(prefab);
                var kabel = EntityManager.HasComponent<ElectricityConnectionData>(prefab)
                    || EntityManager.HasComponent<WaterPipeConnectionData>(prefab);
                if (!road && !kabel) continue;
                if (road && !EntityManager.HasComponent<Owner>(entity))
                    r.GassenStrassen.Add((EntityManager.GetComponentData<Curve>(entity).m_Bezier,
                        EntityManager.HasComponent<NetGeometryData>(prefab)
                            ? EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth : 8f));
                var k = AvLeseKante(entity, strom, wasser);
                if (k == null) continue;
                r.Entitaeten[k.Id] = entity;
                if (road)
                {
                    if (EntityManager.HasComponent<Owner>(entity)) e.Hinderniskanten.Add(k);
                    else if (k.Versorgung) e.Ziele.Add(k);
                }
                else e.Leitungen.Add(k);
            }
            r.SchnappschussMs = VorplanMillis(Stopwatch.GetTimestamp() - uhr);
            return r;
        }

        private bool VorplanGehoertZumEditLot(Entity entity)
        {
            var traeger = EntityManager.HasComponent<ParkingLotCarrierReference>(_editLot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(_editLot).Carrier
                : Entity.Null;
            var gesehen = new HashSet<Entity>();
            while (entity != Entity.Null && gesehen.Add(entity)
                && EntityManager.Exists(entity))
            {
                if (entity == _editLot || entity == traeger) return true;
                if (!EntityManager.HasComponent<Owner>(entity)) break;
                entity = EntityManager.GetComponentData<Owner>(entity).m_Owner;
            }
            return false;
        }

        private static VorplanErgebnis BerechneVorplan(VorplanEingabe e, CancellationToken token)
        {
            var uhr = Stopwatch.GetTimestamp();
            foreach (var g in e.Gassen)
            {
                token.ThrowIfCancellationRequested();
                var kandidaten = new List<Gassenreichweite.Strasse>();
                var boegen = new List<Bezier4x3>();
                foreach (var strasse in e.GassenStrassen)
                {
                    token.ThrowIfCancellationRequested();
                    if (MathUtils.Distance(strasse.Bogen.xz, g.Rand, out _) > GassenSuchweite)
                        continue;
                    var linie = new float2[GassenAbtastung + 1];
                    for (var i = 0; i <= GassenAbtastung; i++)
                        linie[i] = MathUtils.Position(strasse.Bogen, i / (float)GassenAbtastung).xz;
                    kandidaten.Add(new Gassenreichweite.Strasse { Mittellinie = linie,
                        HalbeBreite = strasse.Breite / 2, Kennung = boegen.Count });
                    boegen.Add(strasse.Bogen);
                }
                var gefunden = Gassenreichweite.Finde(g.Rand, g.Rand - g.Innen,
                    kandidaten, out var treffer);
                var mitte = default(float2);
                if (gefunden)
                {
                    var b = boegen[treffer.Kennung];
                    MathUtils.Distance(b.xz, treffer.Mitte, out var t);
                    mitte = MathUtils.Position(b, t).xz;
                    gefunden = math.distance(g.Rand, mitte) > 0.5f
                        && math.distance(mitte, g.Innen) > treffer.HalbeBreiteImStrahl;
                }
                if (!gefunden)
                {
                    e.Plan.Eigene.Remove(g.Kante);
                    e.Plan.Hinderniskanten.Remove(g.Kante);
                    e.Plan.Ziele.Remove(g.Kante);
                    continue;
                }
                VorplanSetzeBogen(g.Kante, VorplanGerade(
                    g.Hinaus ? g.Innen : mitte, g.Hinaus ? mitte : g.Innen));
                g.Kante.KnotenAnStadt = true;
            }
            // Erst nach der Gassenprojektion stehen alle Kursenden fest.
            var knoten = new List<(float2 Punkt, int Id)>();
            var next = -1;
            int FindeKnoten(float2 p)
            {
                foreach (var n in knoten)
                    if (math.distance(n.Punkt, p) <= 0.05f) return n.Id;
                var id = next--;
                knoten.Add((p, id));
                return id;
            }
            foreach (var k in e.Plan.Eigene)
            {
                k.Startknoten = FindeKnoten(k.Startpunkt.xz);
                k.Endknoten = FindeKnoten(k.Endpunkt.xz);
            }
            var r = new VorplanErgebnis { Revision = e.Revision,
                SchnappschussMs = e.SchnappschussMs, Grund = "kein zulaessiger Weg",
                EigeneKanten = e.Plan.Eigene.Count, AlleZiele = e.Plan.Ziele.Count,
                Hinderniskanten = e.Plan.Hinderniskanten.Count,
                Fremdleitungen = e.Plan.Leitungen.Count };
            if (!e.Plan.Ziele.Exists(k => k.Stadt))
            {
                r.Grund = "keine Stadtstrasse im Suchfeld";
                r.RechnungMs = VorplanMillis(Stopwatch.GetTimestamp() - uhr);
                return r;
            }
            e.Plan.Abgebrochen = () => token.IsCancellationRequested;
            r.Trassen.AddRange(VersorgungstrassenPlan.PlaneFolge(e.Plan, out var aus));
            r.EigeneKanten = aus.EigeneKanten;
            r.AlleZiele = aus.AlleZiele;
            r.Hinderniskanten = aus.Hinderniskanten;
            r.Fremdleitungen = aus.Fremdleitungen;
            var diagnose = aus.Beste?.Gruppe;
            if (diagnose == null)
                foreach (var gruppe in aus.Gruppen)
                    if (!gruppe.AnStadt && gruppe.Starts.Count > 0) { diagnose = gruppe; break; }
            r.Starts = diagnose?.Starts.Count ?? 0;
            r.Ziele = diagnose?.Ziele.Count ?? 0;
            r.Huellen = diagnose?.Huellen ?? 0;
            if (aus.Beste != null)
            {
                var w = aus.Beste;
                r.Start = w.Start.xz; r.Ziel = w.Ziel.xz;
                if (e.Entitaeten.TryGetValue(w.Zielkante.Id, out var ziel)) r.Zielkante = ziel;
                r.ZielEigene = !w.Zielkante.Stadt;
                r.Laenge = w.Laenge;
                r.Grund = "Layoutachsen und Welt-Schnappschuss";
            }
            else if (aus.Gruppen.TrueForAll(gruppe => gruppe.AnStadt))
                r.Grund = "alle Layout-Gruppen per Knoten an Stadtstrasse";
            else if (e.Plan.Ziele.Count == 0) r.Grund = "keine Stadtstrasse oder eigene Zielkante";
            r.RechnungMs = VorplanMillis(Stopwatch.GetTimestamp() - uhr);
            return r;
        }
    }
}
