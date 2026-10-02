using System.Collections.Generic;
using System.Globalization;
using Game.Prefabs;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /*
     * WAS KOSTET DIE FAHRT UEBER UNSERE WEGE? - Messung vor dem Umbau.
     *
     * Busse, Lkw und Autos kuerzen ueber PLT-Parkplaetze ab (Nutzer,
     * 2026-10-02). Geplant sind teurere Fahrspuren und niedrigere
     * Geschwindigkeit auf eigenen Klonen. Die heutigen Werte stehen aber in
     * den Vanilla-Assets, nicht im Code: das Dekompilat kennt nur
     * Klassenvorbelegungen (Pathway 40 km/h, Road 100 km/h). Diese Zeile
     * liest die echten Werte je benutztem Netz-Prefab einmal je Sitzung.
     *
     * Kette (Codex-Bericht _codex-durchgang): Netz-Prefab -> RoadData /
     * PathwayData.m_SpeedLimit (m/s) und DefaultNetLane -> NetLaneData
     * .m_PathfindPrefab -> PathfindCarData.m_DrivingCost (Zeit, Verhalten,
     * Geld, Komfort je Meter).
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly HashSet<Entity> _fahrwerteGemessen = new HashSet<Entity>();

        private void MesseFahrwerte()
        {
            foreach (var eintrag in _netRecords)
            {
                var prefab = eintrag.Prefab;
                if (prefab == Entity.Null || !_fahrwerteGemessen.Add(prefab)
                    || !EntityManager.Exists(prefab)) continue;
                var name = _prefabSystem.GetPrefabName(prefab);
                string tempo = "-";
                if (EntityManager.HasComponent<RoadData>(prefab))
                    tempo = Kmh(EntityManager.GetComponentData<RoadData>(prefab).m_SpeedLimit) + " (Strasse)";
                else if (EntityManager.HasComponent<PathwayData>(prefab))
                    tempo = Kmh(EntityManager.GetComponentData<PathwayData>(prefab).m_SpeedLimit) + " (Weg)";
                var kosten = new List<string>();
                if (EntityManager.HasBuffer<DefaultNetLane>(prefab))
                {
                    var spuren = EntityManager.GetBuffer<DefaultNetLane>(prefab, true);
                    var gesehen = new HashSet<Entity>();
                    for (var i = 0; i < spuren.Length; i++)
                    {
                        var spur = spuren[i].m_Lane;
                        if (!EntityManager.HasComponent<CarLaneData>(spur)
                            || !EntityManager.HasComponent<NetLaneData>(spur)) continue;
                        var pfad = EntityManager.GetComponentData<NetLaneData>(spur).m_PathfindPrefab;
                        if (pfad == Entity.Null || !gesehen.Add(pfad)
                            || !EntityManager.HasComponent<PathfindCarData>(pfad)) continue;
                        var k = EntityManager.GetComponentData<PathfindCarData>(pfad).m_DrivingCost.m_Value;
                        kosten.Add(_prefabSystem.GetPrefabName(pfad) + " Zeit "
                            + FwZahl(k.x) + "/Verhalten " + FwZahl(k.y) + "/Geld " + FwZahl(k.z)
                            + "/Komfort " + FwZahl(k.w));
                    }
                }
                Mod.log.Info("PLT-Fahrwerte: '" + name + "' Tempo " + tempo
                    + "; Autospur-Kosten je m: "
                    + (kosten.Count == 0 ? "keine Autospur" : string.Join(" | ", kosten)));
            }
        }

        private static string Kmh(float ms)
            => (ms * 3.6f).ToString("F0", CultureInfo.InvariantCulture) + " km/h";

        private static string FwZahl(float wert)
            => wert.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
