using Game;
using Game.Common;
using Game.Notifications;
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
     * ist eine eigene Entity mit \`Icon.m_Location\`, und \`IconClusterSystem\`
     * ordnet neu ein, was \`Updated\` traegt. Dieses System sitzt dafuer in
     * ModificationEnd zwischen IconCommandSystem (setzt den Ort aus dem
     * Gebaeude) und IconClusterSystem (ordnet ein): rechnet CS2 den Ort neu,
     * steht das Symbol im selben Bild wieder ueber dem Parkplatz.
     */
    public sealed partial class ParkingLotSymbolOrtSystem : GameSystemBase
    {
        /** So hoch ueber dem mittleren Gelaende der Flaeche - ueber Autos und Belag. */
        private const float Hoehe = 2f;

        private EntityQuery _begleiter;

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
            RequireForUpdate(_begleiter);
        }

        [Preserve]
        protected override void OnUpdate()
        {
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

        /** Mitte der Parkplatzflaeche (Mittel ihrer Eckpunkte), knapp ueber dem Gelaende. */
        private bool Mitte(Entity lot, out float3 mitte)
        {
            mitte = default;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Areas.Node>(lot)) return false;
            var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            if (knoten.Length == 0) return false;
            var summe = float3.zero;
            for (var i = 0; i < knoten.Length; i++) summe += knoten[i].m_Position;
            mitte = summe / knoten.Length + new float3(0f, Hoehe, 0f);
            return true;
        }
    }
}
