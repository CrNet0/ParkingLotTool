using System;
using System.Collections.Generic;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Routes;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        // Die TEILE-Meldung zaehlt nur Endknoten. Im Lauf ohne Halte waren
        // 3/3 verbunden, mit Halten 2/3. Die Probe nennt die konkreten
        // Knoten und unterscheidet Enden von seitlichen ConnectedNodes.
        private void AvMeldeBushaltAnschluesse(Entity traeger)
        {
            try
            {
                var eigene = SammleUnsereKanten(traeger).FindAll(KanteNimmtVersorgung);
                var halte = ParkingLotTeilnetz.Bushaltkanten(EntityManager, eigene);
                if (halte.Count == 0) return;
                foreach (var gruppe in SammleVersorgungsgruppen(eigene))
                {
                    var n = gruppe.FindAll(halte.Contains).Count;
                    if (n == 0) continue;
                    var s = new StringBuilder($"PLT-BUSHALT-NETZ: Kanten={gruppe.Count} Haltkanten={n} "
                        + $"EndknotenAnStadt={(AvHaengtAnStadtstrasse(gruppe) ? 1 : 0)} "
                        + $"OHNE-BUSHALT={(Mod.Aus("versorgung-ohne-bushalt") ? 1 : 0)}\n");
                    foreach (var e in gruppe)
                    {
                        var k = EntityManager.GetComponentData<Edge>(e);
                        s.AppendLine($"BUSHALT-KANTE {e} Start={k.m_Start} Ende={k.m_End} Halt={(halte.Contains(e) ? 1 : 0)}");
                        foreach (var knoten in new[] { k.m_Start, k.m_End })
                        {
                            s.AppendLine($"BUSHALT-ENDKNOTEN {knoten} Stadt={(KnotenHatStadtstrasse(knoten) ? 1 : 0)}");
                            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) continue;
                            var ce = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
                            for (var i = 0; i < ce.Length; i++)
                            {
                                var z = ce[i].m_Edge;
                                s.AppendLine($"BUSHALT-CE {knoten} -> {z} lebt={(ParkingLotTeilnetz.Lebt(EntityManager, z) ? 1 : 0)} "
                                    + $"Owner={(EntityManager.HasComponent<Owner>(z) ? EntityManager.GetComponentData<Owner>(z).m_Owner : Entity.Null)} "
                                    + $"Versorgung={(KanteNimmtVersorgung(z) ? 1 : 0)}");
                            }
                        }
                        if (!EntityManager.HasBuffer<ConnectedNode>(e)) continue;
                        var cn = EntityManager.GetBuffer<ConnectedNode>(e, true);
                        for (var i = 0; i < cn.Length; i++)
                            s.AppendLine($"BUSHALT-CN {e} -> {cn[i].m_Node} t={cn[i].m_CurvePosition:F5} "
                                + $"Stadt={(KnotenHatStadtstrasse(cn[i].m_Node) ? 1 : 0)} (kein Endknoten)");
                    }
                    if (Mod.Aus("versorgung-ohne-bushalt"))
                        s.AppendLine("PLT-VERSORGUNG-DIAG OHNE-BUSHALT: ganzes Halt-Netz als Start/Ziel gesperrt; Bedarf bleibt gezaehlt, 0 neue Leitungsanschluesse an dieser Gruppe.");
                    Mod.log.Info(s.ToString());
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn("PLT-BUSHALT-NETZ MESSFEHLER: " + ex.Message);
            }
        }
    }

}
