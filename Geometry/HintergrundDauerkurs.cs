using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundDauerkurs
    {
        /** 22:03: alte Kurvenenden wichen am erhaltenen Original um 0,3975 m
         *  von der Node-Lage ab. Endkorrekturen linear auf alle vier Punkte
         *  verteilen; die Kruemmung bleibt gegenueber der Sehne gleich. */
        public static (float3 A,float3 B,float3 C,float3 D) BindeEnden(
            (float3 A,float3 B,float3 C,float3 D) kurve, float3 start, float3 ende)
        {
            var a = start-kurve.A; var d = ende-kurve.D;
            return (start,kurve.B+math.lerp(a,d,1f/3f),kurve.C+math.lerp(a,d,2f/3f),ende);
        }
    }
}
