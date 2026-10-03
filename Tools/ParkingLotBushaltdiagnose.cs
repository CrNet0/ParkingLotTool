using System;
using System.Collections.Generic;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Routes;
using Unity.Entities;

using Game.Prefabs;
using Game.Tools;
using TransportStop = Game.Routes.TransportStop;
using SubObject = Game.Objects.SubObject;
using SubLane = Game.Net.SubLane;
using CarLane = Game.Net.CarLane;
using PedestrianLane = Game.Net.PedestrianLane;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotVersorgungsdiagnoseSystem
    {
        private bool Enthalten(Entity parent, Entity kind)
        {
            if (!Existiert(parent) || !EntityManager.HasBuffer<SubObject>(parent)) return false;
            var b = EntityManager.GetBuffer<SubObject>(parent, true);
            for (var i = 0; i < b.Length; i++) if (b[i].m_SubObject == kind) return true;
            return false;
        }

        private void Bushaltdaten(string kopf)
        {
            var q = GetEntityQuery(ComponentType.ReadOnly<TransportStop>());
            using var halte = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            // Ein entfernter Owner entfernt den alten SubObject-Eintrag
            // nicht automatisch (SubObjectReferences laeuft nur Created/
            // Deleted). Auch ehemalige Lot-/Traeger-Puffer sichtbar machen.
            var pufferVon = new Dictionary<Entity, List<Entity>>();
            if (_bilder <= 2)
            {
                var pq = GetEntityQuery(ComponentType.ReadOnly<SubObject>());
                using var eltern = pq.ToEntityArray(Unity.Collections.Allocator.Temp);
                foreach (var e in eltern)
                {
                    var b = EntityManager.GetBuffer<SubObject>(e, true);
                    for (var i = 0; i < b.Length; i++)
                    {
                        var kind = b[i].m_SubObject;
                        if (!EntityManager.HasComponent<TransportStop>(kind)) continue;
                        if (!pufferVon.TryGetValue(kind, out var liste))
                            pufferVon[kind] = liste = new List<Entity>();
                        liste.Add(e);
                    }
                }
            }
            var anzahl = 0;
            foreach (var h in halte)
            {
                var parent = EntityManager.HasComponent<Attached>(h)
                    ? EntityManager.GetComponentData<Attached>(h).m_Parent : Entity.Null;
                var plt = EntityManager.HasComponent<ParkingLotPartRelation>(h);
                if (!plt && !_physisch.Contains(parent)) continue;
                var s = new StringBuilder($"BUSHALT {(plt ? "PLT" : "VANILLA-VERGLEICH")} {Zustand(h)} Typen=[{Inventar(h)}]\n");
                Daten<Temp>(h, s); Daten<TransportStop>(h, s); Daten<Attached>(h, s);
                Daten<Owner>(h, s); Daten<PrefabRef>(h, s); Daten<Transform>(h, s);
                Daten<Game.Objects.NetObject>(h, s); Daten<Placeholder>(h, s);
                Daten<Attachment>(h, s); Daten<OwnerDefinition>(h, s); s.AppendLine();
                if (pufferVon.TryGetValue(h, out var puffer))
                    foreach (var e in puffer)
                        s.AppendLine($"BUSHALT-PUFFER {Zustand(e)} Parent={(e == parent ? 1 : 0)} "
                            + $"Owner={(EntityManager.HasComponent<Owner>(h) && EntityManager.GetComponentData<Owner>(h).m_Owner == e ? 1 : 0)}");
                if (plt) Daten<ParkingLotPartRelation>(h, s);
                s.AppendLine($"BUSHALT-PARENT {Zustand(parent)} SubObject={(Enthalten(parent, h) ? 1 : 0)} Typen=[{Inventar(parent)}]");
                Daten<Temp>(parent, s); Daten<Edge>(parent, s); Daten<Curve>(parent, s);
                Daten<Upgraded>(parent, s);
                Daten<Owner>(parent, s); Daten<Placeholder>(parent, s); Daten<Attachment>(parent, s); s.AppendLine();
                if (Existiert(parent) && EntityManager.HasComponent<Temp>(parent))
                {
                    var original = EntityManager.GetComponentData<Temp>(parent).m_Original;
                    s.AppendLine($"BUSHALT-PARENT-ORIGINAL {Zustand(original)} SubObject={(Enthalten(original, h) ? 1 : 0)}");
                }
                if (Existiert(parent) && !Enthalten(parent, h)
                    && !EntityManager.HasComponent<Deleted>(h) && !EntityManager.HasComponent<Temp>(h))
                    s.AppendLine("KANDIDAT BUSHALT fehlt Parent.SubObject-Gegenrichtung; Leser=Attach/CompositionSelect");
                if (!Existiert(parent) && parent != Entity.Null)
                    s.AppendLine("KANDIDAT BUSHALT toter Attached.Parent");
                if (EntityManager.HasComponent<Owner>(h))
                {
                    var owner = EntityManager.GetComponentData<Owner>(h).m_Owner;
                    s.AppendLine($"BUSHALT-OWNER {Zustand(owner)} SubObject={(Enthalten(owner, h) ? 1 : 0)}");
                    if (EntityManager.HasComponent<Created>(h) && !EntityManager.HasComponent<Temp>(h)
                        && (!Existiert(owner) || !EntityManager.HasBuffer<SubObject>(owner)))
                        s.AppendLine("KANDIDAT BUSHALT fehlt Owner.SubObject; Leser=SubObjectReferences.Created");
                }
                if (EntityManager.HasComponent<PrefabRef>(h))
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(h).m_Prefab;
                    s.AppendLine($"BUSHALT-PREFAB {Zustand(prefab)}");
                    Daten<NetObjectData>(prefab, s); Daten<PlaceableObjectData>(prefab, s);
                    Daten<TransportStopData>(prefab, s); Daten<RouteConnectionData>(prefab, s); s.AppendLine();
                    Muss<Transform>(h, "CompositionSelect.GetSubObjectFlags.Attached", s);
                }
                if (EntityManager.HasBuffer<ConnectedRoute>(h))
                {
                    var routes = EntityManager.GetBuffer<ConnectedRoute>(h, true);
                    s.AppendLine($"BUSHALT-ROUTEN Anzahl={routes.Length}");
                    for (var i = 0; i < routes.Length; i++)
                    {
                        var w = routes[i].m_Waypoint;
                        s.AppendLine($"BUSHALT-WAYPOINT [{i}] {Zustand(w)}");
                        Daten<Owner>(w, s); Daten<Temp>(w, s); Daten<Connected>(w, s);
                        Daten<RouteLane>(w, s); Daten<AccessLane>(w, s); s.AppendLine();
                        if (!Existiert(w)) { s.AppendLine("KANDIDAT BUSHALT toter ConnectedRoute.Waypoint"); continue; }
                        if (EntityManager.HasComponent<RouteLane>(w))
                        {
                            var r = EntityManager.GetComponentData<RouteLane>(w);
                            Bushaltspur(r.m_StartLane, "Route.Start", s);
                            Bushaltspur(r.m_EndLane, "Route.End", s);
                        }
                        if (EntityManager.HasComponent<AccessLane>(w))
                            Bushaltspur(EntityManager.GetComponentData<AccessLane>(w).m_Lane, "Access", s);
                    }
                }
                if (Existiert(parent) && EntityManager.HasBuffer<SubLane>(parent))
                {
                    var lanes = EntityManager.GetBuffer<SubLane>(parent, true);
                    var car = 0; var walk = 0;
                    for (var i = 0; i < lanes.Length; i++)
                    {
                        var lane = lanes[i].m_SubLane;
                        if (EntityManager.HasComponent<CarLane>(lane)) car++;
                        if (EntityManager.HasComponent<PedestrianLane>(lane)) walk++;
                        if (_bilder <= 2 && (EntityManager.HasComponent<CarLane>(lane)
                            || EntityManager.HasComponent<PedestrianLane>(lane)))
                            Bushaltspur(lane, "Parent.SubLane[" + i + "]", s);
                    }
                    s.AppendLine($"BUSHALT-SPURBILANZ SubLane={lanes.Length} CarLane={car} PedestrianLane={walk}");
                }
                SchreibeDaten(kopf + "\n" + s);
                anzahl++;
            }
            // Auch tote Nicht-Halte im Strassenpuffer koennen den direkten
            // PrefabRef-Zugriff in CompositionSelect ausloesen.
            foreach (var e in _physisch)
            {
                if (!Existiert(e) || !EntityManager.HasBuffer<SubObject>(e)) continue;
                var b = EntityManager.GetBuffer<SubObject>(e, true);
                for (var i = 0; i < b.Length; i++)
                    if (!Existiert(b[i].m_SubObject) || !EntityManager.HasComponent<PrefabRef>(b[i].m_SubObject))
                        SchreibeDaten($"{kopf} KANDIDAT SUBOBJECT {e}[{i}] -> {Zustand(b[i].m_SubObject)} fehlt PrefabRef; Leser=CompositionSelect");
            }
            SchreibeDaten(kopf + $" BUSHALT-BILD END Halte={anzahl}");
        }

        private void Bushaltspur(Entity e, string rolle, StringBuilder s)
        {
            s.AppendLine($"BUSHALT-LANE {rolle} {Zustand(e)} Typen=[{Inventar(e)}]");
            Daten<Lane>(e, s); Daten<Curve>(e, s); Daten<Owner>(e, s);
            Daten<PrefabRef>(e, s); Daten<CarLane>(e, s); Daten<PedestrianLane>(e, s); s.AppendLine();
            if (Existiert(e) && EntityManager.HasComponent<PrefabRef>(e))
            {
                var p = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                var system = World.GetExistingSystemManaged<PrefabSystem>();
                if (system != null && system.TryGetPrefab<PrefabBase>(p, out var asset) && asset != null)
                    s.AppendLine($"BUSHALT-LANEPREFAB {Zustand(p)} '{asset.name}'");
                Daten<NetLaneData>(p, s); Daten<CarLaneData>(p, s); s.AppendLine();
            }
            if (e != Entity.Null && !Existiert(e)) s.AppendLine("KANDIDAT BUSHALT toter Route-/AccessLane-Verweis");
        }
    }
}
