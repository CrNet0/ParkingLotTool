using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotErsatzabriss
    {
        internal static List<Entity> Eigene(EntityManager em, Entity alt, Entity lot, Entity traeger)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var alle = q.ToEntityArray(Allocator.Temp);
            var erhalten = new HashSet<Entity>();
            if (em.HasBuffer<ParkingLotErhaltenerKurs>(alt))
                foreach (var k in em.GetBuffer<ParkingLotErhaltenerKurs>(alt,true))
                { erhalten.Add(k.Bestand.Kante); erhalten.Add(k.Bestand.Start); erhalten.Add(k.Bestand.Ende); }
            return HintergrundRuecknahme.Eigene(alle,lot,traeger,
                e => em.GetComponentData<Owner>(e).m_Owner,erhalten.Contains);
        }
        internal static IEnumerable<int> Schritte(EntityManager em, Entity alt, Entity lot, Entity traeger)
        {
            ParkingLotNetzerhalt.EntferneGelieheneVerweise(em,alt,lot,traeger);
            var eigene = Eigene(em,alt,lot,traeger);
            var knoten = new List<Entity>(); int kanten = 0;
            foreach (var e in eigene)
            {
                if (em.HasComponent<Node>(e)) { knoten.Add(e); continue; }
                if (em.HasComponent<Edge>(e)) kanten++;
                if (ParkingLotNetzRueckweg.Lebt(em,e)) em.AddComponent<Deleted>(e);
                yield return 0;
            }
            // Fix 1d680b6: ALLE Kanten vorher, ALLE Knoten im selben Bild.
            // Besitzer folgen ihren Kindern ohne weitere Unterbrechung.
            foreach (var e in knoten)
            {
                if (em.HasBuffer<ConnectedEdge>(e))
                    foreach (var k in em.GetBuffer<ConnectedEdge>(e,true))
                        if (ParkingLotNetzRueckweg.Lebt(em,k.m_Edge))
                            throw new InvalidOperationException($"Ersatzknoten {e} hat noch lebende Kante {k.m_Edge}; Ruecknahme nicht freigegeben.");
            }
            foreach (var e in knoten)
                if (ParkingLotNetzRueckweg.Lebt(em,e)) em.AddComponent<Deleted>(e);
            if (ParkingLotNetzRueckweg.Lebt(em,traeger)) em.AddComponent<Deleted>(traeger);
            if (ParkingLotNetzRueckweg.Lebt(em,lot)) em.AddComponent<Deleted>(lot);
            ParkingLotNetzRueckweg.Melde($"Ersatzabriss: eigene Kanten {kanten}, eigene Knoten {knoten.Count}, eigene Kinder {eigene.Count}; Besitzer zuletzt, erhaltene Originale 0 geloescht.");
        }
    }
}
