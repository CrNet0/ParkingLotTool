using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundKurspruefung
    {
        // Der Aufrufer projiziert auf jede tatsaechlich materialisierte Kurve.
        // Volle Weltlage: eine um 183 m versetzte Kurve darf trotz identischem
        // XZ-Verlauf nie als Erfolg zaehlen. Messraster: 17 Punkte, 5 cm.
        public static bool LageGedeckt(float3 soll, IEnumerable<float3> kandidaten)
        {
            foreach (var p in kandidaten)
                if (math.distance(soll, p) <= .05f) return true;
            return false;
        }
        public static bool KurveGleich((float3 A,float3 B,float3 C,float3 D) ist,
            (float3 A,float3 B,float3 C,float3 D) soll, bool umgekehrt = false)
        {
            if (umgekehrt) soll = (soll.D,soll.C,soll.B,soll.A);
            return math.distance(ist.A,soll.A) <= .05f
                && math.distance(ist.B,soll.B) <= .05f
                && math.distance(ist.C,soll.C) <= .05f
                && math.distance(ist.D,soll.D) <= .05f;
        }

        public static bool FahrtempoStimmt(float meterProSekunde)
            => math.abs(meterProSekunde - 25f / 3.6f) <= .01f;

        public static bool NullhoeheFixieren(float2 elevation)
            => math.all(elevation == float2.zero);
    }
}
