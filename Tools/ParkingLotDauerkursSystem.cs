using System;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    internal struct ParkingLotGeplanterDauerkurs : IComponentData
    {
        public NetCourse Kurs;
    }

    /** Eigenstaendig konstruierte, bereits geteilte Permanent-Kurse gehen
     *  direkt an GenerateNodes/Edges. CourseSplit.CanConnect (2515) kann
     *  Road/MarkerPathway-Anschluesse verwerfen; MergePositions (2452) loescht
     *  dabei die ID. Hintergrund9 misst die Layerregel an Game.dll: 2/2
     *  gemischte Layerpaare abgelehnt, 2/2 gleichartige angenommen. GenerateNodes
     *  (1021) und GenerateEdges (1237) nehmen eine echte Permanent-Node-ID
     *  unmittelbar, auch bei verschiedenem Owner.
     *  Keine Eingriffe in Vanilla-Systeme/Prefabs oder fertige Netze. */
    [DisableAutoCreation]
    internal sealed partial class ParkingLotDauerkursSystem : GameSystemBase
    {
        private EntityQuery _geplant;
        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _geplant = GetEntityQuery(ComponentType.ReadOnly<ParkingLotGeplanterDauerkurs>(),
                ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<ParkingLotAuftragsdefinition>());
        }
        internal static void Plane(EntityManager em, Entity definition)
        {
            var d = em.GetComponentData<CreationDefinition>(definition);
            if ((d.m_Flags & CreationFlags.Permanent) == 0 || !em.HasComponent<ParkingLotAuftragsdefinition>(definition))
                throw new InvalidOperationException("Dauerkurs ohne eigenen Permanent-Auftrag.");
            var kurs = em.GetComponentData<NetCourse>(definition);
            // Ein fehlgeschlagener Pruefer darf keine baubare Definition
            // hinterlassen, auch wenn der Aufrufer die Ausnahme behandelt.
            em.RemoveComponent<NetCourse>(definition);
            Pruefe(em, kurs);
            // Nur explizite Pathway-Anschluesse an Road-Knoten gehen direkt.
            // Ihre ID darf auch bei spaeter geaenderten Layern nicht verloren
            // gehen. Alle anderen Kurse behalten dessen Terrainhoehen
            // und AuxiliaryNet-Ausgabe (Versorgung).
            if (!IstDirekt(em,d.m_Prefab,kurs))
            {
                foreach (var p in new[] {kurs.m_StartPosition,kurs.m_EndPosition})
                    if (p.m_Entity != Entity.Null && !NetUtils.CanConnect(
                        em.GetComponentData<NetData>(d.m_Prefab),
                        em.GetComponentData<NetData>(em.GetComponentData<PrefabRef>(p.m_Entity).m_Prefab)))
                        throw new InvalidOperationException($"CourseSplit-Anschluss {p.m_Entity} hat unvereinbare Layer; 0 Ausgabe dieses Kurses.");
                em.AddComponentData(definition,kurs);
                return;
            }
            em.AddComponentData(definition, new ParkingLotGeplanterDauerkurs { Kurs = kurs });
        }
        internal static bool IstDirekt(EntityManager em, Entity prefab, NetCourse kurs)
        {
            if (!em.HasComponent<PathwayData>(prefab)
                || em.HasBuffer<AuxiliaryNet>(prefab) && em.GetBuffer<AuxiliaryNet>(prefab,true).Length != 0) return false;
            foreach (var p in new[] {kurs.m_StartPosition,kurs.m_EndPosition})
                if (em.HasComponent<Node>(p.m_Entity) && em.HasComponent<PrefabRef>(p.m_Entity))
                {
                    var ziel = em.GetComponentData<NetData>(em.GetComponentData<PrefabRef>(p.m_Entity).m_Prefab);
                    if ((ziel.m_RequiredLayers & Layer.Road) != 0) return true;
                }
            return false;
        }
        internal static void Pruefe(EntityManager em, NetCourse kurs)
        {
            foreach (var p in new[] {kurs.m_StartPosition, kurs.m_EndPosition})
            {
                if (!math.all(math.isfinite(p.m_Position))) throw new InvalidOperationException("Nichtendliche Dauerkurslage.");
                if (p.m_Entity == Entity.Null) continue;
                if (!em.Exists(p.m_Entity) || em.HasComponent<Deleted>(p.m_Entity)
                    || em.HasComponent<Temp>(p.m_Entity) || !em.HasComponent<Node>(p.m_Entity)
                    || !em.HasComponent<PrefabRef>(p.m_Entity)
                    || !em.HasBuffer<ConnectedEdge>(p.m_Entity)
                    || !math.all(math.isfinite(em.GetComponentData<Node>(p.m_Entity).m_Position))
                    || math.distance(em.GetComponentData<Node>(p.m_Entity).m_Position,p.m_Position) > .001f)
                    throw new InvalidOperationException($"Dauerkursanschluss {p.m_Entity} ist kein lebender Originalknoten in Soll-Lage; 0 Ausgabe dieses Kurses.");
            }
        }
        [Preserve]
        protected override void OnUpdate()
        {
            using var definitionen = _geplant.ToEntityArray(Allocator.Temp);
            // Vollstaendige Portion vor ihrer ersten Ausgabe pruefen.
            try
            {
                foreach (var e in definitionen)
                    Pruefe(EntityManager,EntityManager.GetComponentData<ParkingLotGeplanterDauerkurs>(e).Kurs);
            }
            catch (InvalidOperationException e)
            {
                ParkingLotNetzRueckweg.Melde($"Dauerkursportion verworfen: {e.Message}; 0 NetCourse ausgegeben, Abnahme erzwingt Rueckweg.");
                return;
            }
            foreach (var e in definitionen)
            {
                EntityManager.AddComponentData(e,EntityManager.GetComponentData<ParkingLotGeplanterDauerkurs>(e).Kurs);
                EntityManager.RemoveComponent<ParkingLotGeplanterDauerkurs>(e);
            }
        }
    }
}
