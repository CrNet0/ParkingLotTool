using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotVersorgungsdiagnoseSystem
    {
        internal bool Misst => _aktiv && _bilder > 0 && _bilder <= 120;

        internal void Phasenmarke(string phase, bool daten = false)
        {
            if (!Misst) return;
            var kopf = $"PLT-VERSORGUNG-PHASE Lauf={_lauf} Bild={_bilder} Frame={UnityEngine.Time.frameCount} {phase}";
            SchreibeDaten(kopf + " BEGIN");
            try
            {
                // An jeder Grenze erst alle bereits geplanten Jobs beenden.
                // BEGIN ist vorher dauerhaft geschrieben. Damit heisst END
                // mehr als 'OnUpdate hat nur den Burst-Job eingereiht'.
                EntityManager.CompleteAllTrackedJobs();
                if (daten && _bilder <= 2) Datenstand(kopf);
                if (daten) Bushaltdaten(kopf);
                SchreibeDaten(kopf + " END");
            }
            catch (Exception ex) { SchreibeDaten(kopf + " MESSFEHLER " + ex); }
        }

        private static void SchreibeDaten(string s)
        {
            ParkingLotSchrittmarke.Setze(s);
            ParkingLotSchrittmarke.Versorgungsbild(s);
            ParkingLotLiveLog.Zeile(s);
            Mod.log.Info(s);
        }

        private string Inventar(Entity e)
        {
            if (!Existiert(e)) return "Entity tot";
            using var typen = EntityManager.GetComponentTypes(e, Allocator.Temp);
            var namen = new List<string>();
            foreach (var t in typen) namen.Add(t.GetManagedType()?.FullName ?? t.ToString());
            namen.Sort(StringComparer.Ordinal);
            return string.Join(",", namen);
        }

        private static string Felder<T>(T wert) where T : struct
        {
            var s = new StringBuilder();
            foreach (var f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
                s.Append(f.Name).Append('=').Append(Werttext(f.GetValue(wert), 0)).Append(' ');
            return s.ToString();
        }

        private static string Werttext(object v, int tiefe)
        {
            if (v == null) return "null";
            if (v is Entity entity) return entity.ToString();
            if (v is IFormattable form) return form.ToString(null, CultureInfo.InvariantCulture);
            // CompositionFlags hat kein eigenes ToString. Nur der Typname
            // verschweigt alle drei Flags; die Probe misst deshalb Unterfelder.
            if (v.GetType().IsValueType && tiefe < 3)
            {
                var s = new StringBuilder("{");
                foreach (var f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    s.Append(f.Name).Append('=').Append(Werttext(f.GetValue(v), tiefe + 1)).Append(' ');
                return s.Append('}').ToString();
            }
            return v.ToString();
        }

        private void Daten<T>(Entity e, StringBuilder s) where T : unmanaged, IComponentData
        {
            s.Append(typeof(T).Name).Append('=');
            if (!Existiert(e) || !EntityManager.HasComponent<T>(e)) s.Append("fehlt ");
            else s.Append('{').Append(Felder(EntityManager.GetComponentData<T>(e))).Append("} ");
        }

        private void Muss<T>(Entity e, string grund, StringBuilder s) where T : unmanaged, IComponentData
        {
            if (!Existiert(e) || !EntityManager.HasComponent<T>(e))
                s.AppendLine($"KANDIDAT {Zustand(e)} fehlt {typeof(T).Name}; Leser={grund}");
        }

        private void Datenstand(string kopf)
        {
            ErfasseUmfeld(_wurzeln);
            var prefabs = new List<(Entity Entity, string Rolle)>();
            var gesehen = new HashSet<(Entity, string)>();
            var kompositionen = new HashSet<Entity>();
            foreach (var e in _physisch)
            {
                var s = new StringBuilder($"DATEN {Zustand(e)} Typen=[{Inventar(e)}]\n");
                if (Existiert(e))
                {
                    Daten<Temp>(e, s); Daten<Composition>(e, s); Daten<NodeGeometry>(e, s);
                    Daten<EdgeGeometry>(e, s); Daten<StartNodeGeometry>(e, s); Daten<EndNodeGeometry>(e, s);
                    s.AppendLine();
                    if (EntityManager.HasComponent<PrefabRef>(e))
                        prefabs.Add((EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, "Netzprefab"));
                    Besitzer(e, s, prefabs);
                    if (EntityManager.HasComponent<Composition>(e))
                    {
                        var c = EntityManager.GetComponentData<Composition>(e);
                        foreach (var ce in new[] { c.m_Edge, c.m_StartNode, c.m_EndNode })
                            if (kompositionen.Add(ce)) Kompositionsdaten(ce, s, prefabs);
                    }
                    if (EntityManager.HasComponent<Orphan>(e))
                    {
                        var ce = EntityManager.GetComponentData<Orphan>(e).m_Composition;
                        if (kompositionen.Add(ce)) Kompositionsdaten(ce, s, prefabs);
                    }
                    if (EntityManager.HasBuffer<Game.Net.SubLane>(e))
                    {
                        var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(e, true);
                        s.AppendLine($"SUBLANES {e} Anzahl={lanes.Length}");
                        for (var i = 0; i < lanes.Length; i++)
                        {
                            var lane = lanes[i].m_SubLane;
                            s.AppendLine($"SUBLANE [{i}] {Zustand(lane)} Typen=[{Inventar(lane)}]");
                            Daten<Lane>(lane, s); Daten<Curve>(lane, s); Daten<Owner>(lane, s);
                            Daten<PrefabRef>(lane, s); Daten<EdgeLane>(lane, s); Daten<SlaveLane>(lane, s);
                            s.AppendLine();
                            if (!Existiert(lane)) continue;
                            // SecondaryLane liest diese drei Daten auch bei
                            // unveraenderten Fremdspuren (383-386 im Dekompilat).
                            Muss<Lane>(lane, "SecondaryLane", s); Muss<Curve>(lane, "SecondaryLane", s);
                            Muss<PrefabRef>(lane, "SecondaryLane", s);
                            if (EntityManager.HasComponent<PrefabRef>(lane))
                                prefabs.Add((EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab, "Spurprefab"));
                        }
                    }
                    if (EntityManager.HasBuffer<Game.Objects.SubObject>(e))
                    {
                        var objects = EntityManager.GetBuffer<Game.Objects.SubObject>(e, true);
                        for (var i = 0; i < objects.Length; i++)
                        {
                            var o = objects[i].m_SubObject;
                            s.AppendLine($"SUBOBJECT [{i}] {Zustand(o)} Typen=[{Inventar(o)}]");
                            Muss<PrefabRef>(o, "CompositionSelect.GetSubObjectFlags", s);
                        }
                    }
                    if (EntityManager.HasBuffer<Game.Buildings.ConnectedBuilding>(e))
                    {
                        var buildings = EntityManager.GetBuffer<Game.Buildings.ConnectedBuilding>(e, true);
                        for (var i = 0; i < buildings.Length; i++)
                            Gebaeude(buildings[i].m_Building, s, prefabs);
                    }
                }
                // Jede Entity einzeln spuelen: ein Absturz spaeter in der
                // Referenzkette vernichtet die vorherigen Datensaetze nicht.
                SchreibeDaten(kopf + "\n" + s);
            }
            while (prefabs.Count > 0)
            {
                var p = prefabs[prefabs.Count - 1];
                prefabs.RemoveAt(prefabs.Count - 1);
                if (!gesehen.Add((p.Entity, p.Rolle))) continue;
                Prefabdaten(p.Entity, p.Rolle, kopf, prefabs);
            }
            var fluss = new StringBuilder();
            int tot = 0, asym = 0, waise = 0;
            Flusszeilen(fluss, ref tot, ref asym, ref waise);
            SchreibeDaten(kopf + $" FLOW ToteReferenzen={tot} Asymmetrien={asym}\n" + fluss);
        }

        private void Besitzer(Entity e, StringBuilder s, List<(Entity, string)> prefabs)
        {
            var besucht = new HashSet<Entity> { e };
            var kind = e;
            while (Existiert(kind) && EntityManager.HasComponent<Owner>(kind))
            {
                var o = EntityManager.GetComponentData<Owner>(kind).m_Owner;
                var subNet = Existiert(o) && EntityManager.HasBuffer<Game.Net.SubNet>(o);
                var drin = subNet && ParkingLotPuffer.Hat(EntityManager.GetBuffer<Game.Net.SubNet>(o, true), kind);
                s.AppendLine($"OWNER {kind} -> {Zustand(o)} SubNet={(subNet ? "vorhanden" : "fehlt")} KindEnthalten={(drin ? 1 : 0)} Typen=[{Inventar(o)}]");
                if (EntityManager.HasComponent<Created>(kind) && !EntityManager.HasComponent<Temp>(kind) && !subNet)
                    s.AppendLine("KANDIDAT fehlt Owner.SubNet; Leser=SubNetReferences.Created");
                if (Existiert(o) && EntityManager.HasComponent<Game.Objects.Transform>(o))
                    Muss<PrefabRef>(o, "Lane.FindAnchors (Transform vorhanden)", s);
                if (!besucht.Add(o)) { s.AppendLine("KANDIDAT OWNER-ZYKLUS"); break; }
                if (besucht.Count > 32) { s.AppendLine("OWNER-KETTE Grenze=32 erreicht"); break; }
                if (Existiert(o) && EntityManager.HasComponent<Game.Buildings.Building>(o)) Gebaeude(o, s, prefabs);
                kind = o;
            }
        }

        private void Gebaeude(Entity e, StringBuilder s, List<(Entity, string)> prefabs)
        {
            s.AppendLine($"GEBAEUDE {Zustand(e)} Typen=[{Inventar(e)}]");
            Daten<Game.Buildings.Building>(e, s); Daten<Game.Objects.Transform>(e, s);
            Daten<ElectricityBuildingConnection>(e, s); Daten<WaterPipeBuildingConnection>(e, s); s.AppendLine();
            if (!Existiert(e)) return;
            if (EntityManager.HasComponent<ElectricityBuildingConnection>(e))
            {
                var c = EntityManager.GetComponentData<ElectricityBuildingConnection>(e);
                foreach (var edge in new[] { c.m_ProducerEdge, c.m_ConsumerEdge, c.m_ChargeEdge, c.m_DischargeEdge })
                    if (edge != Entity.Null) _flusskanten.Add(edge);
            }
            if (EntityManager.HasComponent<WaterPipeBuildingConnection>(e))
            {
                var c = EntityManager.GetComponentData<WaterPipeBuildingConnection>(e);
                foreach (var edge in new[] { c.m_ProducerEdge, c.m_ConsumerEdge })
                    if (edge != Entity.Null) _flusskanten.Add(edge);
            }
            if (EntityManager.HasComponent<PrefabRef>(e))
                prefabs.Add((EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, "Gebaeudeprefab"));
            if (EntityManager.HasComponent<Game.Buildings.Building>(e))
            {
                var road = EntityManager.GetComponentData<Game.Buildings.Building>(e).m_RoadEdge;
                s.AppendLine($"BUILDING-ROAD {e} -> {Zustand(road)}");
                if (road != Entity.Null)
                {
                    Muss<EdgeGeometry>(road, "RoadConnection", s);
                    Muss<StartNodeGeometry>(road, "RoadConnection", s);
                    Muss<EndNodeGeometry>(road, "RoadConnection", s);
                }
            }
        }

        private void Kompositionsdaten(Entity e, StringBuilder s, List<(Entity, string)> prefabs)
        {
            s.AppendLine($"KOMPOSITION {Zustand(e)} Typen=[{Inventar(e)}]");
            Daten<NetCompositionData>(e, s); Daten<PrefabRef>(e, s); s.AppendLine();
            Muss<NetCompositionData>(e, "Lane/SecondaryLane (Created: Daten erst in Mod4)", s);
            if (Existiert(e) && EntityManager.HasBuffer<NetCompositionPiece>(e))
            {
                var pieces = EntityManager.GetBuffer<NetCompositionPiece>(e, true);
                for (var i = 0; i < pieces.Length; i++)
                    if ((pieces[i].m_PieceFlags & NetPieceFlags.HasMesh) != 0
                        && (pieces[i].m_SectionFlags & NetSectionFlags.Hidden) == 0)
                    {
                        var p = pieces[i].m_Piece;
                        Muss<MeshData>(p, "NetCompositionMeshRef.HasMesh", s);
                        if (!Existiert(p) || !EntityManager.HasBuffer<MeshMaterial>(p))
                            s.AppendLine($"KANDIDAT {p} fehlt MeshMaterial; Leser=NetCompositionMeshRef.HasMesh");
                    }
            }
            Puffer<NetCompositionPiece>(e, s, x => x.m_Piece, "Piece", prefabs);
            Puffer<NetCompositionLane>(e, s, x => x.m_Lane, "Spurprefab", prefabs);
            Puffer<NetCompositionCrosswalk>(e, s, x => x.m_Lane, "Spurprefab", prefabs);
            if (Existiert(e) && EntityManager.HasComponent<PrefabRef>(e))
                prefabs.Add((EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, "Netzprefab"));
        }

        private void Puffer<T>(Entity e, StringBuilder s, Func<T, Entity> ziel,
            string rolle, List<(Entity, string)> prefabs, bool verfolgen = true) where T : unmanaged, IBufferElementData
        {
            var name = typeof(T).Name;
            if (!Existiert(e) || !EntityManager.HasBuffer<T>(e)) { s.AppendLine(name + "=fehlt"); return; }
            var b = EntityManager.GetBuffer<T>(e, true);
            s.AppendLine($"{name} Anzahl={b.Length}");
            for (var i = 0; i < b.Length; i++)
            {
                var z = ziel(b[i]);
                s.AppendLine($"{name}[{i}] {Felder(b[i])} Ziel={Zustand(z)}");
                if (verfolgen) prefabs.Add((z, rolle));
            }
        }

        private void Prefabdaten(Entity e, string rolle, string kopf, List<(Entity, string)> prefabs)
        {
            var s = new StringBuilder($"PREFAB Rolle={rolle} {Zustand(e)} Typen=[{Inventar(e)}]\n");
            var system = World.GetExistingSystemManaged<PrefabSystem>();
            if (Existiert(e) && system != null && system.TryGetPrefab<PrefabBase>(e, out var asset)) s.AppendLine("Name='" + asset.name + "'");
            if (Existiert(e))
            {
                Daten<NetData>(e, s); Daten<NetGeometryData>(e, s); Daten<RoadData>(e, s);
                Daten<LocalConnectData>(e, s); Daten<ElectricityConnectionData>(e, s); Daten<WaterPipeConnectionData>(e, s);
                Daten<BuildingData>(e, s);
                Daten<NetPieceData>(e, s); Daten<MeshData>(e, s); Daten<NetLaneData>(e, s);
                Daten<CarLaneData>(e, s); Daten<UtilityLaneData>(e, s); Daten<SecondaryLaneData>(e, s); s.AppendLine();
                if (rolle == "Netzprefab") { Muss<NetData>(e, "CompositionSelect", s); Muss<NetGeometryData>(e, "CompositionSelect", s); }
                if (rolle == "Piece") Muss<NetPieceData>(e, "NetCompositionHelpers", s);
                if (rolle == "Spurprefab") Muss<NetLaneData>(e, "Lane/SecondaryLane", s);
                if (rolle == "Sekundaerspur") Muss<SecondaryLaneData>(e, "SecondaryLane.CreateSecondaryLane", s);
                var quelle = World.GetExistingSystemManaged<ParkingLotFahrprefabSystem>()?.QuelleFuer(e) ?? Entity.Null;
                if (quelle != Entity.Null)
                {
                    s.AppendLine($"PF-VERGLEICH Klon={e} Vanilla={Zustand(quelle)} Typen=[{Inventar(quelle)}]");
                    // Metadaten duerfen fehlen. Deshalb Unterschiede messen,
                    // ohne jeden UI-/Spawn-Unterschied als Defekt zu behandeln.
                    Daten<NetData>(quelle, s); Daten<NetGeometryData>(quelle, s); Daten<NetPieceData>(quelle, s);
                    Daten<MeshData>(quelle, s); Daten<NetLaneData>(quelle, s); s.AppendLine();
                    prefabs.Add((quelle, rolle));
                }
                Puffer<NetGeometrySection>(e, s, x => x.m_Section, "Section", prefabs);
                Puffer<NetSubSection>(e, s, x => x.m_SubSection, "Section", prefabs);
                Puffer<NetSectionPiece>(e, s, x => x.m_Piece, "Piece", prefabs);
                Puffer<NetPieceLane>(e, s, x => x.m_Lane, "Spurprefab", prefabs);
                Puffer<DefaultNetLane>(e, s, x => x.m_Lane, "Spurprefab", prefabs);
                Puffer<SecondaryNetLane>(e, s, x => x.m_Lane, "Sekundaerspur", prefabs);
                Puffer<AuxiliaryNetLane>(e, s, x => x.m_Prefab, "Spurprefab", prefabs);
                Puffer<LodMesh>(e, s, x => x.m_LodMesh, "LOD", prefabs);
                if (rolle == "Netzprefab" && !EntityManager.HasBuffer<NetGeometryComposition>(e))
                    s.AppendLine($"KANDIDAT {e} fehlt NetGeometryComposition; Leser=CompositionSelect");
                // Der Cache darf viele fremde Kompositionen enthalten. Seine
                // Verweise messen, ohne weitere Netznachbarschaften zu oeffnen.
                Puffer<NetGeometryComposition>(e, s, x => x.m_Composition,
                    "Kompositionsvorrat", prefabs, false);
            }
            SchreibeDaten(kopf + "\n" + s);
        }
    }
}
