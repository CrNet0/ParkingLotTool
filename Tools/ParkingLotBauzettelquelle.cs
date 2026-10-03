using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Alles, was in einen Bauzettel geht - unabhaengig davon, woher es kommt.
     *
     * Nach einem Bau stammt es aus dem laufenden Werkzeug
     * (`WriteBuildReceipt`), bei einem verwaisten Parkplatz aus dem
     * Bauprotokoll. Geschrieben wird in beiden Faellen von
     * `SchreibeBauzettel` - so kann es kein zweites, abweichendes Format
     * geben.
     */
    internal sealed class Bauzettelquelle
    {
        internal LayoutSettings Settings;
        internal float3[] Punkte;
        internal double MedianWidth;
        internal bool GreenMedian;
        internal double CrossBays;
        internal bool SurfaceRoadOn;
        internal bool SurfaceDecorationOn;
        internal bool SurfaceApronOn;
        internal bool BayIcons;
        internal string ZoningWinkelmodus;
        internal double ZoningReglerwinkel;
        internal int ZoningAussentiefeVorwahl;
        internal double? ZoningAusrichtwinkel;
        internal double? Ausrichtwinkel;
        internal IReadOnlyList<ParkingLotToolSystem.Ausrichtzuweisung> Ausrichtungen;
        internal IReadOnlyList<Teilflaechenschnitt> Trennschnitte;
        internal IReadOnlyList<ParkingGeometry.Zoningflaeche> Zoningflaechen;
        internal IReadOnlyList<(float2 A, float2 B, bool Links, bool Aus)> Seitenplan;
        internal IReadOnlyList<ParkingGeometry.RandzoningLinie> Randzoning;
        internal string FlaecheStrasse;
        internal string FlaecheDekoration;
        internal string FlaecheZoning;

        /** Nur bei der Wiederherstellung: die Vegetationswahl als JSON. */
        internal string VegetationAusProtokoll;
    }

}
