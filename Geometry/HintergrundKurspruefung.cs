using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundKurspruefung
    {
        // Der Aufrufer projiziert auf jede tatsaechlich materialisierte Kurve.
        // Endproben bleiben 3D: eine um 183 m versetzte Kurve darf trotz
        // identischem XZ-Verlauf nie als Erfolg zaehlen. Innenproben folgen
        // derselben Vanilla-Regel wie der Kontrollpunktabgleich: 17, 5 cm.
        public static bool LageGedeckt(float3 soll, IEnumerable<float3> kandidaten, bool innenhoeheVanilla = false)
        {
            if (!math.all(math.isfinite(soll))) return false;
            foreach (var p in kandidaten)
                if (math.all(math.isfinite(p))
                    && (innenhoeheVanilla ? math.distance(soll.xz,p.xz) : math.distance(soll,p)) <= .05f) return true;
            return false;
        }

        // CourseSplit.SampleCourseHeight 1675-1724 passt bei !StraightEdges
        // die beiden inneren Y-Werte an, auch bei ParentMesh=0. ParentMesh
        // sperrt NUR GenerateEdges 1427-1450. Dort kann bei bodengleichen,
        // nicht beidseitig gebundenen Kursen zusaetzlich NetUtils anpassen.
        // StraightEdges bildet hingegen die Gerade aus den festen Enden
        // (1675-1700); ohne Bodenanpassung bleibt ihre Innenhoehe pruefbar.
        // Lauf 13:01, Kurs 20: Enden/XZ identisch, inneres Y 16,129 cm anders.
        // Road/Pathway ist keine Zulassungsregel: dieselben Flags entscheiden.
        public static bool InnenhoeheVanilla(bool geradeKanten, bool bodengleich,
            bool beideParentMesh, bool flattenTerrain, bool besitzer)
            => !geradeKanten || bodengleich && !beideParentMesh && (!flattenTerrain || besitzer);
        // Vorgeteilte Dauerkurse sehen nur GenerateEdges, nicht mehr CourseSplit.
        // Daher bleibt innen Y auch bei !StraightEdges streng, sobald beide
        // ParentMesh-Enden die Bodenanpassung sperren (GenerateEdges 1427ff).
        public static bool InnenhoeheGeneratoren(bool bodengleich, bool beideParentMesh,
            bool flattenTerrain, bool besitzer)
            => bodengleich && !beideParentMesh && (!flattenTerrain || besitzer);
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
