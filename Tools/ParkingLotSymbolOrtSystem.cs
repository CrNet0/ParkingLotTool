using Game;
using Game.Common;
using Game.Notifications;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * SYMBOLE DES BEGLEITERS UEBER DEN PARKPLATZ (Nutzer 2026-10-05).
     *
     * CS2 haengt Hinweise wie "Mangel an Arbeitskraeften" an den unsichtbaren
     * Wirtschaftsbegleiter und setzt sie an dessen Ort. Der steht an der
     * Zufahrt, weil er dort seine Strassenanbindung braucht - die Symbole
     * erschienen deshalb auf der Strasse statt ueber dem Parkplatz. Nutzer:
     * "Das kann zu Verwirrung fuehren."
     *
     * Der Begleiter bleibt, wo er ist; nur seine Symbole wandern. Jedes Symbol
     * ist eine eigene Entity mit `Icon.m_Location`, und `IconClusterSystem`
     * ordnet neu ein, was `Updated` traegt. Dieses System sitzt dafuer in
     * ModificationEnd zwischen IconCommandSystem (setzt den Ort aus dem
     * Gebaeude) und IconClusterSystem (ordnet ein): rechnet CS2 den Ort neu,
     * steht das Symbol im selben Bild wieder ueber dem Parkplatz.
     */
    public sealed partial class ParkingLotSymbolOrtSystem : GameSystemBase
    {
        /** So hoch ueber dem mittleren Gelaende der Flaeche - ueber Autos und Belag. */
        private const float Hoehe = 2f;

        private EntityQuery _begleiter, _alleBegleiter;
        private Game.Prefabs.PrefabSystem _prefabs;
        private Game.Simulation.TerrainSystem _gelaende;
        private string _letzterBericht;
        private int _bilder;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _begleiter = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<IconElement>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _alleBegleiter = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _prefabs = World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
            _gelaende = World.GetOrCreateSystemManaged<Game.Simulation.TerrainSystem>();
            RequireForUpdate(_alleBegleiter);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (++_bilder % 512 == 0) Berichte();
            if (_begleiter.IsEmptyIgnoreFilter) return;
            using var begleiter = _begleiter.ToEntityArray(Allocator.Temp);
            foreach (var b in begleiter)
            {
                var lot = EntityManager.GetComponentData<ParkingLotPartRelation>(b).Lot;
                if (!Mitte(lot, out var ziel)) continue;
                var symbole = EntityManager.GetBuffer<IconElement>(b, true).ToNativeArray(Allocator.Temp);
                foreach (var s in symbole)
                {
                    var e = s.m_Icon;
                    if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Icon>(e)
                        || EntityManager.HasComponent<Deleted>(e)) continue;
                    var icon = EntityManager.GetComponentData<Icon>(e);
                    if (math.distancesq(icon.m_Location, ziel) < 0.01f) continue;
                    icon.m_Location = ziel;
                    EntityManager.SetComponentData(e, icon);
                    if (!EntityManager.HasComponent<Updated>(e)) EntityManager.AddComponent<Updated>(e);
                }
                symbole.Dispose();
            }
        }

        /**
         * Je Parkplatz: Plaetze, Symbole am Begleiter (Prefabname) und wo sie
         * stehen. Nur bei Aenderung ins Log - Nutzerbefund 2026-10-05: bei zwei
         * von drei Parkplaetzen im selben Gebiet erschien kein Symbol.
         */
        private void Berichte()
        {
            using var alle = _alleBegleiter.ToEntityArray(Allocator.Temp);
            var teile = new System.Collections.Generic.List<string>();
            foreach (var b in alle)
            {
                var lot = EntityManager.GetComponentData<ParkingLotPartRelation>(b).Lot;
                var plaetze = EntityManager.Exists(lot) && EntityManager.HasComponent<ParkingLotEconomyData>(lot)
                    ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot).Capacity : -1;
                var text = "Lot " + lot.Index + " (" + plaetze + " Plaetze, Begleiter " + b.Index + "): ";
                if (!EntityManager.HasBuffer<IconElement>(b) || EntityManager.GetBuffer<IconElement>(b, true).Length == 0)
                    text += "kein Symbol am Begleiter";
                else
                {
                    var namen = new System.Collections.Generic.List<string>();
                    foreach (var s in EntityManager.GetBuffer<IconElement>(b, true))
                    {
                        if (!EntityManager.Exists(s.m_Icon) || !EntityManager.HasComponent<Game.Prefabs.PrefabRef>(s.m_Icon)) continue;
                        var ort = EntityManager.HasComponent<Icon>(s.m_Icon)
                            ? EntityManager.GetComponentData<Icon>(s.m_Icon).m_Location : float3.zero;
                        namen.Add(_prefabs.GetPrefabName(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(s.m_Icon).m_Prefab)
                            + " @" + ort.x.ToString("0") + "/" + ort.z.ToString("0"));
                    }
                    text += namen.Count + " Symbol(e) " + string.Join(", ", namen);
                }
                teile.Add(text);
            }
            teile.Sort();
            var bericht = string.Join("; ", teile);
            if (bericht == _letzterBericht) return;
            _letzterBericht = bericht;
            Mod.log.Info("PLT-Symbole: " + bericht);
        }

        /**
         * Mitte der Parkplatzflaeche (Mittel ihrer Eckpunkte), knapp ueber dem
         * Gelaende AN DER MITTE. Nur das Hoehenmittel der Ecken lag bei grossen
         * Flaechen am Hang unter der Oberflaeche (2097 Plaetze: Symbol da, aber
         * nicht zu sehen).
         */
        private bool Mitte(Entity lot, out float3 mitte)
        {
            mitte = default;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Areas.Node>(lot)) return false;
            var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            if (knoten.Length == 0) return false;
            var summe = float3.zero;
            for (var i = 0; i < knoten.Length; i++) summe += knoten[i].m_Position;
            mitte = summe / knoten.Length;
            var hoehen = _gelaende.GetHeightData();
            var boden = Game.Simulation.TerrainUtils.SampleHeight(ref hoehen, mitte);
            if (math.isfinite(boden)) mitte.y = math.max(mitte.y, boden);
            mitte.y += Hoehe;
            return true;
        }
    }
}
