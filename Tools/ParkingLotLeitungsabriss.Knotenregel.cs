using Game.Common;
using Game.Net;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotLeitungsabrissSystem
    {
        // Gemeinsame Produktionsregel fuer Abriss und Neubau-Sperre.
        // Eigene Datei, damit der bestehende ECS-Test dieselbe Regel ausfuehrt
        // statt sie im Testdouble nachzubauen (Befund: 3 fehlende Typen im Test).
        internal static bool TraegtNichtsMehr(EntityManager em, Entity knoten)
        {
            if (!em.HasBuffer<ConnectedEdge>(knoten)) return false;
            foreach (var v in em.GetBuffer<ConnectedEdge>(knoten, true))
            {
                var e = v.m_Edge;
                if (!em.Exists(e)) continue;
                if (em.HasComponent<Deleted>(e)) continue;
                if (!em.HasComponent<Edge>(e)) continue;
                var kante = em.GetComponentData<Edge>(e);
                if (kante.m_Start == knoten || kante.m_End == knoten)
                    return false;
            }
            return true;
        }
    }
}
