using System;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /** Eine fehlende Lane-Erzeugung darf kein erfolgreicher Neubau sein. */
        internal bool HatGebauteFahrspuren(Entity traeger)
        {
            if (!EntityManager.Exists(traeger) || !EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
                return false;
            foreach (var netz in EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true))
            {
                var kante = netz.m_SubNet;
                if (!EntityManager.HasComponent<Edge>(kante) || EntityManager.HasComponent<Deleted>(kante)
                    || EntityManager.HasComponent<Temp>(kante) || !EntityManager.HasBuffer<Game.Net.SubLane>(kante))
                    continue;
                foreach (var lane in EntityManager.GetBuffer<Game.Net.SubLane>(kante, true))
                    if (EntityManager.HasComponent<Game.Net.CarLane>(lane.m_SubLane)) return true;
            }
            return false;
        }

        /** Nur lesen. Der Sync stellt Prefabs ausschliesslich per Edit/Neubau um. */
        internal bool BrauchtFahrwegeNeubau(Entity traeger)
        {
            if (!EntityManager.Exists(traeger) || !EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
                return false;
            var netze = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            foreach (var netz in netze)
            {
                var kante = netz.m_SubNet;
                if (!EntityManager.HasComponent<Edge>(kante) || EntityManager.HasComponent<Deleted>(kante)
                    || EntityManager.HasComponent<Temp>(kante) || !EntityManager.HasComponent<PrefabRef>(kante))
                    continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab;
                var name = _prefabSystem.GetPrefabName(prefab);
                // Unveraenderte Zoningkanten duerfen NICHT fuer diese Migration
                // abgerissen werden. Ihr stabil benanntes RoadPrefab hat neue
                // Vorgaben, gespeicherte Lanes prueft die Fahrwertmessung.
                if (name.StartsWith("PLT Zoningstrasse (", StringComparison.Ordinal)) continue;
                if (ParkingLotFahrregeln.IstFahrweg(name)) return true;
                if (!name.StartsWith("PLT ", StringComparison.Ordinal)
                    || !EntityManager.HasBuffer<Game.Net.SubLane>(kante)) continue;
                var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(kante, true);
                foreach (var lane in lanes)
                {
                    var spur = lane.m_SubLane;
                    if (!EntityManager.HasComponent<Game.Net.CarLane>(spur)
                        || !EntityManager.HasComponent<PrefabRef>(spur)) continue;
                    var spurPrefab = EntityManager.GetComponentData<PrefabRef>(spur).m_Prefab;
                    if (!EntityManager.HasComponent<NetLaneData>(spurPrefab)) return true;
                    var pfad = EntityManager.GetComponentData<NetLaneData>(spurPrefab).m_PathfindPrefab;
                    if (!EntityManager.HasComponent<PathfindCarData>(pfad)) return true;
                    // Verbindungs-Lanes koennen aus dem angrenzenden PLT-Weg
                    // stammen (LaneSystem.CreateNodeLane waehlt Quelle/Ziel).
                    // Deshalb eigener Klonbereich, nicht dieselbe Wurzel.
                    if (!ParkingLotFahrregeln.IstTeilname(_prefabSystem.GetPrefabName(spurPrefab))
                        || !ParkingLotFahrregeln.IstTeilname(_prefabSystem.GetPrefabName(pfad)))
                        return true;
                    var ist = EntityManager.GetComponentData<PathfindCarData>(pfad).m_DrivingCost.m_Value;
                    var soll = ParkingLotFahrregeln.Fahrkosten.ToPathfindCosts().m_Value;
                    if (Unity.Mathematics.math.any(Unity.Mathematics.math.abs(ist - soll) > 0.00001f)) return true;
                }
            }
            return false;
        }
    }
}
