using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Was die Pflanzenplanung von einer Laterne wissen muss: wo sie steht, wie
     * sie gedreht ist und wie hoch ihr Mast ist (Prefab-Daten, siehe
     * ParkingLotLaternen.LaternenKoerperFuer).
     *
     * 2026-10-04: Hier stand kurz eine Nachrechnung von CS2s Kollision
     * (OverrideSystem), weil ich verdeckte Buesche den Laternen zuschrieb. Sie
     * ragten aber in die Stadtstrasse daneben; die Laterne verdeckt keinen
     * Busch. Der Unterschied je Art kam aus `VegetationSpecies.Tree` (TreeData).
     */
    public struct LaternenKoerper
    {
        public float2 Position;
        /** Lokale +Z-Achse des Modells in der Welt, Einheitsvektor. */
        public float2 Vorwaerts;
        /** m_LegSize - x Mastdicke, y Masthoehe (gemessen: 0,30 x 5,00 m bei allen Strassenlaternen). */
        public float3 Bein;
        /** m_Bounds.max */
        public float3 Max;

        /** Bis hierher reicht der Mast; darueber sitzt die Leuchte. */
        public float Masthoehe => Bein.y > 0f ? Bein.y : Max.y;
    }
}
