using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Tools;
using Game.Prefabs;
using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    // CourseSplit.AddCourse 3754-3770 erzeugt ab Abschnitt 2 neue Entities
    // ohne Deleted/PLT-Marke. Der Abschluss liegt nach deren Wiedergabe,
    // aber VOR GenerateNodes/Edges: Lebensdauer der Eingaben, keine Reparatur
    // an materialisierten Netzen. Nur explizit angemeldete PLT-Besitzer.
    [DisableAutoCreation]
    internal sealed partial class ParkingLotDefinitionsendeSystem : GameSystemBase
    {
        private readonly Dictionary<Entity,int> _besitzer = new Dictionary<Entity,int>();
        private EntityQuery _kurse;

        internal void Merke(Entity owner, int auftrag)
        {
            if (owner != Entity.Null) _besitzer[owner] = auftrag;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _kurse = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.ReadOnly<NetCourse>(),ComponentType.ReadOnly<Updated>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_besitzer.Count == 0) return;
            using var kurse = _kurse.ToEntityArray(Allocator.Temp);
            var hauptkurse = new List<(Entity Prefab,Unity.Mathematics.float3 A,Unity.Mathematics.float3 D,int Auftrag)>();
            foreach (var e in kurse)
            {
                var d = EntityManager.GetComponentData<CreationDefinition>(e);
                if (!_besitzer.TryGetValue(d.m_Owner,out int auftrag) || (d.m_Flags & CreationFlags.Permanent) == 0) continue;
                var c = EntityManager.GetComponentData<NetCourse>(e);
                hauptkurse.Add((d.m_Prefab,MathUtils.Position(c.m_Curve,c.m_StartPosition.m_CourseDelta),
                    MathUtils.Position(c.m_Curve,c.m_EndPosition.m_CourseDelta),auftrag));
            }
            int n = 0;
            foreach (var e in kurse)
            {
                if (EntityManager.HasComponent<Deleted>(e)) continue;
                var d = EntityManager.GetComponentData<CreationDefinition>(e);
                bool eigen = _besitzer.TryGetValue(d.m_Owner,out int auftrag);
                bool hilfskurs = false;
                // GetAuxDefinition 4109-4115 verliert m_Owner. Der Hilfskurs
                // traegt dafuer OwnerDefinition mit Hauptprefab und dessen
                // beiden EXAKTEN Endpunkten (3637-3643). Nur diese Herkunft
                // plus deklariertes AuxiliaryNet berechtigt den Abschluss.
                if (!eigen && d.m_Owner == Entity.Null && EntityManager.HasComponent<OwnerDefinition>(e))
                {
                    var owner = EntityManager.GetComponentData<OwnerDefinition>(e);
                    foreach (var h in hauptkurse)
                    {
                        if (owner.m_Prefab != h.Prefab || !owner.m_Position.Equals(h.A)
                            || !owner.m_Rotation.Equals(new Unity.Mathematics.float4(h.D,0))
                            || !EntityManager.HasBuffer<AuxiliaryNet>(h.Prefab)) continue;
                        foreach (var a in EntityManager.GetBuffer<AuxiliaryNet>(h.Prefab,true))
                            if (a.m_Prefab == d.m_Prefab) { hilfskurs = true; auftrag = h.Auftrag; }
                    }
                }
                if (!HintergrundDefinitionsleben.Abschliessen(eigen,(d.m_Flags & CreationFlags.Permanent) != 0,hilfskurs)) continue;
                ParkingLotToolSystem.NurDiesesBild(EntityManager,e);
                EntityManager.AddComponentData(e,new ParkingLotAuftragsdefinition { Auftrag = auftrag });
                n++;
            }
            _besitzer.Clear();
            if (n > 0) ParkingLotNetzRueckweg.Melde($"CourseSplit-Lebensdauer: {n} abgeleitete eigene Permanent-Definitionen auf dieses Bild begrenzt; 0 fremde Definitionen veraendert.");
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context context)
        { base.OnGameLoaded(context); _besitzer.Clear(); }
    }
}
