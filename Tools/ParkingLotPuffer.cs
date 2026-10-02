using Game.Net;
using Game.Simulation;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Suchen in CS2-Puffern OHNE LINQ. `DynamicBuffer<T>` implementiert
     * IEnumerable<T>.GetEnumerator nur als NotImplementedException - `.Any()`
     * und Co. scheitern im Spiel, im Test aber nicht (Ingame-Log 2026-10-03,
     * ParkingLotVersorgungsdiagnoseSystem). `foreach` geht, weil es den
     * Struct-Enumerator nimmt.
     */
    internal static class ParkingLotPuffer
    {
        internal static bool Hat(DynamicBuffer<ConnectedEdge> puffer, Entity kante)
        {
            for (var i = 0; i < puffer.Length; i++) if (puffer[i].m_Edge == kante) return true;
            return false;
        }

        internal static bool Hat(DynamicBuffer<ConnectedNode> puffer, Entity knoten)
        {
            for (var i = 0; i < puffer.Length; i++) if (puffer[i].m_Node == knoten) return true;
            return false;
        }

        internal static bool Hat(DynamicBuffer<ConnectedFlowEdge> puffer, Entity kante)
        {
            for (var i = 0; i < puffer.Length; i++) if (puffer[i].m_Edge == kante) return true;
            return false;
        }

        internal static bool Hat(DynamicBuffer<Game.Net.SubNet> puffer, Entity netz)
        {
            for (var i = 0; i < puffer.Length; i++) if (puffer[i].m_SubNet == netz) return true;
            return false;
        }
    }
}
