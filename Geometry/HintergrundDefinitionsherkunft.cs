using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundDefinitionsherkunft
    {
        // CourseSplit.AddCourse 3719/3735/3754: Quellseed + Abschnittsindex;
        // neue Abschnitte haben keine PLT-Marke. Nur derselbe Prefab, Owner
        // und ein XZ-Teilstueck der vorher angemeldeten Eingabe belegen die
        // Herkunft. Die obere Seedgrenze ist die Zahl ALLER aktuellen Kurse.
        // Y wird in SampleCourseHeight geaendert; dies ist ausschliesslich
        // Lebensdauerzuordnung, KEINE Freigabe eines gebauten Netzes.
        public static bool TempTeil(bool prefabGleich, bool besitzerlos,
            int quellseed, int teilseed, int kursanzahl, float kontrollXZAbstand)
            => prefabGleich && besitzerlos && kursanzahl > 0
                && unchecked((uint)(teilseed-quellseed)) < (uint)kursanzahl
                && kontrollXZAbstand <= .05f;

        public static bool TempHilfskurs(bool besitzerlos, bool permanent,
            bool hauptkursBelegt, bool deklariert)
            => besitzerlos && !permanent && hauptkursBelegt && deklariert;

        public static float KontrollXZAbstand((float3 A,float3 B,float3 C,float3 D) a,
            (float3 A,float3 B,float3 C,float3 D) b)
        {
            if (!math.all(math.isfinite(a.A.xz)) || !math.all(math.isfinite(a.B.xz))
                || !math.all(math.isfinite(a.C.xz)) || !math.all(math.isfinite(a.D.xz))
                || !math.all(math.isfinite(b.A.xz)) || !math.all(math.isfinite(b.B.xz))
                || !math.all(math.isfinite(b.C.xz)) || !math.all(math.isfinite(b.D.xz))) return float.PositiveInfinity;
            return math.max(math.max(math.distance(a.A.xz,b.A.xz),math.distance(a.B.xz,b.B.xz)),
                math.max(math.distance(a.C.xz,b.C.xz),math.distance(a.D.xz,b.D.xz)));
        }
    }
}
