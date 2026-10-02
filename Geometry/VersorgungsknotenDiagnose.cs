using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    internal static class VersorgungsknotenDiagnose
    {
        // Diagnose: genau 2 geometrische Ziele statt Projektion plus 17 Proben.
        // Keine nachtraegliche Verschiebung einer bereits geprueften Trasse.
        internal static IEnumerable<float2> Ziele(Versorgungskante k)
        {
            if (k.Startknoten != 0) yield return k.Startpunkt.xz;
            if (k.Endknoten != 0) yield return k.Endpunkt.xz;
        }

        internal static bool IstEndpunkt(Versorgungskante k, float2 p)
            => k.Startknoten != 0 && math.distance(k.Startpunkt.xz, p) <= 0.05f
                || k.Endknoten != 0 && math.distance(k.Endpunkt.xz, p) <= 0.05f;
    }
}
