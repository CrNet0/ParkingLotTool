using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    /** Nur lesen: ein erhaltener Zoning-Besitzeranker ist nicht der neue
     * Traeger. Nach einem Vanilla-Replace sind dessen SubNet-Eintraege kein
     * vollstaendiges Inventar. Die Probe misst Anker, Relation und Replace
     * getrennt; an fertigen Netzen wird kein Puffer ergaenzt. */
    internal static class ParkingLotTeilnetz
    {
        internal static bool Lebt(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e)
                && !em.HasComponent<Temp>(e);

        internal static bool GehoertZu(EntityManager em, Entity e, Entity lot, Entity traeger)
        {
            var gesehen = new HashSet<Entity>();
            while (Lebt(em, e) && gesehen.Add(e))
            {
                if (e == lot || e == traeger) return true;
                if (em.HasComponent<ParkingLotPartRelation>(e))
                {
                    var r = em.GetComponentData<ParkingLotPartRelation>(e);
                    if (lot != Entity.Null && r.Lot == lot
                        || traeger != Entity.Null && r.Carrier == traeger) return true;
                }
                if (!em.HasComponent<Owner>(e)) break;
                e = em.GetComponentData<Owner>(e).m_Owner;
            }
            return false;
        }

        internal static List<Entity> Kanten(EntityManager em, Entity lot, Entity traeger)
        {
            var r = new List<Entity>();
            if (!Lebt(em, traeger)) return r;
            var gesehen = new HashSet<Entity>();
            void Merke(Entity e)
            {
                if (Lebt(em, e) && em.HasComponent<Edge>(e)
                    && gesehen.Add(e)) r.Add(e);
            }
            if (em.HasBuffer<SubNet>(traeger))
            {
                var netze = em.GetBuffer<SubNet>(traeger, true);
                for (var i = 0; i < netze.Length; i++) Merke(netze[i].m_SubNet);
            }
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
                if (!gesehen.Contains(e) && GehoertZu(em, e, lot, traeger)) Merke(e);
            return r;
        }

        internal static HashSet<Entity> Bushaltkanten(EntityManager em, IEnumerable<Entity> eigene = null)
        {
            var r = new HashSet<Entity>();
            var eigeneKanten = eigene == null ? null : new HashSet<Entity>(eigene);
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(),
                ComponentType.ReadOnly<Game.Objects.Attached>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var halte = q.ToEntityArray(Allocator.Temp);
            foreach (var h in halte)
            {
                var e = em.GetComponentData<Game.Objects.Attached>(h).m_Parent;
                if (!em.HasComponent<ParkingLotPartRelation>(h)
                    && (eigeneKanten == null || !eigeneKanten.Contains(e))) continue;
                if (Lebt(em, e) && em.HasComponent<Edge>(e)) r.Add(e);
            }
            return r;
        }
    }
}
