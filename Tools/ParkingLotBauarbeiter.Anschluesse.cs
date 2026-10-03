using System;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private void HintergrundKnotenanschluss(ref Anschluss anschluss, ref float3 lage)
        {
            if (EntityManager.HasComponent<Game.Net.Node>(anschluss.Entity))
            {
                lage = HintergrundAbgleich.Anschlusslage(lage,EntityManager.GetComponentData<Game.Net.Node>(anschluss.Entity).m_Position,true);
                return;
            }
            if (anschluss.Entity != Entity.Null) return; // Explizite Stadtkantenteilung bleibt bestehen.
            var kandidaten = new List<(int Id,float3 Lage)>();
            foreach (var e in _erhalteneZoningteile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e) && EntityManager.HasComponent<Game.Net.Node>(e))
                    kandidaten.Add((e.Index,EntityManager.GetComponentData<Game.Net.Node>(e).m_Position));
            int index = HintergrundAbgleich.ErhaltenerKnoten(lage.xz,kandidaten);
            foreach (var e in _erhalteneZoningteile)
                if (e.Index == index)
                {
                    anschluss = new Anschluss { Entity = e };
                    lage = EntityManager.GetComponentData<Game.Net.Node>(e).m_Position;
                    return;
                }
            if (index == -2) ParkingLotNetzRueckweg.Melde($"Mehrdeutiger erhaltener Anschluss bei {lage}; keine Original-ID geraten.");
        }

        internal void HintergrundMeldeAnschluesse()
        {
            StelleVersorgungsanschluesseWiederHer(_lotCarrier);
            // Erhaltene Strassen behalten Kante/Owner/Geometrie. Vanilla
            // erneuert ihre alten Fahrspuren aus den bereits fertigen Klonen;
            // diese echten Werte gehoeren zur 30-Bilder-Nachpruefung.
            var n = MeldeErhalteneZoningteileAn();
            ParkingLotNetzRueckweg.Melde($"Erhaltene Zoningteile bei Vanilla angemeldet: {n}; 0 Owner-/Kurven-/Upgrade-Aenderungen.");
        }

        internal bool HintergrundAnschluesseDa(out int ist, out int soll)
        {
            ist = 0; soll = _versorgungsanschluesse.Count;
            foreach (var a in _versorgungsanschluesse)
                foreach (var e in _versorgungNeueKanten)
                {
                    if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e)
                        || !EntityManager.HasBuffer<ConnectedNode>(e)
                        || !EntityManager.HasBuffer<ConnectedEdge>(a.Fremdknoten)) continue;
                    bool hin = ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedNode>(e,true), a.Fremdknoten);
                    bool zurueck = ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedEdge>(a.Fremdknoten,true), e);
                    if (!hin || !zurueck) continue;
                    // Gemeinsamer Vanilla-Graphhelfer erzeugt fehlende
                    // Simulations-Flusskanten; keine physische Netzkante
                    // oder deren Geometrie wird direkt geaendert.
                    VerbindeVersorgungsgraph(e,a.Fremdknoten,
                        EntityManager.GetComponentData<PrefabRef>(a.Fremdknoten).m_Prefab,a.Leitung);
                    ist++; break;
                }
            return ist == soll;
        }

        internal void HintergrundRueckwegFluss(Entity lot)
            => ParkingLotNetzRueckweg.StelleFlussWiederHer(EntityManager,lot,
                (kante,knoten,prefab) => VerbindeVersorgungsgraph(kante,knoten,prefab,"Rueckweg"));

        internal bool HintergrundBeobachtetFluss => _avPhase == AvPhase.Nachmessen || _zoningNamenRest > 0;
        internal void HintergrundPflegeFluss()
        { PflegeAutoVersorgungsmessung(); PflegeZoningNamenswache(); }
    }
}
