using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal struct ParkingLotAuftragsdefinition : IComponentData
    {
        public int Auftrag;
    }

    public sealed partial class ParkingLotToolSystem
    {
        private ParkingLotDefinitionsmodus _definitionsmodus;
        private int _definitionsauftrag;
        internal bool BearbeitetLot(Entity lot) => _editLot == lot || _pendingEditLot == lot;

        /** Gemeinsamer Abschluss ALLER Definitionen. Vanilla liest den Owner
         *  in Nodes 100-123, Edges 1534-1542, Areas 264-269, Objects 1251-1253.
         *  Im Temp-Pfad bleibt der bisherige besitzerlose Kurs unveraendert. */
        private void SchliesseDefinition(Entity definition, Entity owner)
        {
            EntityManager.AddComponentData(definition,
                new ParkingLotAuftragsdefinition { Auftrag = _definitionsauftrag });
            if (_definitionsmodus != ParkingLotDefinitionsmodus.Permanent)
            {
                // Feste Definitionen gibt es auch im Werkzeugpfad (Haltestellen).
                if ((EntityManager.GetComponentData<CreationDefinition>(definition).m_Flags
                        & CreationFlags.Permanent) != 0)
                    NurDiesesBild(EntityManager, definition);
                return;
            }
            var data = EntityManager.GetComponentData<CreationDefinition>(definition);
            data.m_Flags |= CreationFlags.Permanent;
            data.m_Owner = owner;
            if (EntityManager.HasComponent<NetCourse>(definition))
            {
                var kurs = EntityManager.GetComponentData<NetCourse>(definition);
                if (ParkingLotTool.Geometry.HintergrundKurspruefung.NullhoeheFixieren(kurs.m_Elevation))
                {
                    // Vanilla GenerateEdges 1427-1442: beide ParentMesh >= 0
                    // ueberspringen die erneute Terrainanpassung. GenerateNodes
                    // 1487-1489 erzeugt dadurch Elevation(0) vor References;
                    // Null erreicht keine Brueckenschwelle. Unsere Definition
                    // hat keinen LocalCurveCache: GenerateNodes 657/672 reicht
                    // deshalb hasCachedPosition=false weiter; 1597ff legt
                    // folglich keinen LocalTransformCache am Knoten an.
                    kurs.m_StartPosition.m_ParentMesh = 0;
                    kurs.m_EndPosition.m_ParentMesh = 0;
                    EntityManager.SetComponentData(definition,kurs);
                }
            }
            if (EntityManager.HasComponent<ObjectDefinition>(definition) && owner != Entity.Null)
            {
                data.m_Attached = owner;
                data.m_Flags |= CreationFlags.Attach;
            }
            EntityManager.SetComponentData(definition, data);
            if (EntityManager.HasComponent<NetCourse>(definition))
                World.GetOrCreateSystemManaged<ParkingLotDefinitionsendeSystem>().Merke(owner,_definitionsauftrag);
            NurDiesesBild(EntityManager, definition);
        }

        /**
         * FESTE DEFINITIONEN LEBEN NUR EIN BILD - wie bei Vanilla
         * (ZoneSpawnSystem: Archetyp CreationDefinition + Updated + Deleted).
         * Die Generatoren lesen sie im selben Bild trotz `Deleted`, CleanUp
         * zerstoert sie am Bildende. Ohne `Deleted` blieb sie fuer immer
         * liegen; das Freigabe-Tor des Hintergrund-Neubaus sah danach ewig
         * "1 fremde Definition" und wartete endlos (Ingame 2026-10-03).
         */
        internal static void NurDiesesBild(EntityManager em, Entity definition)
        {
            if (!em.HasComponent<Deleted>(definition)) em.AddComponent<Deleted>(definition);
        }
    }
}
