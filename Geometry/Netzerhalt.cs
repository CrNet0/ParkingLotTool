using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class Netzerhalt
    {
        public sealed class Teil<T>
        {
            public T Kante, Start, Ende;
            public (float3 A,float3 B,float3 C,float3 D) Kurve;
            public bool PrefabGleich, Verbunden, SeitenGleich;
            public float2 Bereich;
        }

        // Das Layout liefert gerichtete gerade XZ-Kurse. Terrain-Y der
        // bestehenden Kurve ist kein Aenderungsauftrag; im Schnappschuss
        // wird es anschliessend unabhaengig mit vier 3D-Punkten gesichert.
        public static List<Teil<T>> FindeKette<T>(float2 von, float2 nach, T stadt,
            bool stadtAmEnde, T leer, IEnumerable<Teil<T>> kandidaten)
        {
            var teile = new List<Teil<T>>();
            float2 v = nach-von;
            float laenge = math.lengthsq(v);
            if (!math.isfinite(laenge) || laenge < 1f) return teile;
            var a = new float3(von.x,0,von.y); var d = new float3(nach.x,0,nach.y);
            var soll = (a,math.lerp(a,d,1f/3f),math.lerp(a,d,2f/3f),d);
            foreach (var p in kandidaten)
            {
                float start = math.clamp(math.dot(p.Kurve.A.xz-von,v)/laenge,0,1);
                float ende = math.clamp(math.dot(p.Kurve.D.xz-von,v)/laenge,0,1);
                var s = HintergrundAbgleich.Schnitt(soll,start,ende);
                float abstand = math.max(math.max(math.distance(p.Kurve.A.xz,s.A.xz),math.distance(p.Kurve.B.xz,s.B.xz)),
                    math.max(math.distance(p.Kurve.C.xz,s.C.xz),math.distance(p.Kurve.D.xz,s.D.xz)));
                if (!math.all(math.isfinite(p.Kurve.A)) || !math.all(math.isfinite(p.Kurve.B))
                    || !math.all(math.isfinite(p.Kurve.C)) || !math.all(math.isfinite(p.Kurve.D))
                    || !Unveraendert(p.PrefabGleich,ende-start > 1e-6f,true,p.Verbunden,p.SeitenGleich,abstand)) continue;
                p.Bereich = new float2(start,ende); teile.Add(p);
            }
            teile.Sort((x,y) => x.Bereich.x.CompareTo(y.Bereich.x));
            float erreicht = 0; T letzter = leer;
            foreach (var p in teile)
            {
                if (math.abs(p.Bereich.x-erreicht) > 1e-5f
                    || !EqualityComparer<T>.Default.Equals(letzter,leer) && !EqualityComparer<T>.Default.Equals(letzter,p.Start))
                    return new List<Teil<T>>();
                erreicht = p.Bereich.y; letzter = p.Ende;
            }
            if (teile.Count == 0 || math.abs(erreicht-1f) > 1e-5f) return new List<Teil<T>>();
            T anschluss = stadtAmEnde ? teile[teile.Count-1].Ende : teile[0].Start;
            if (!Unveraendert(true,true,EqualityComparer<T>.Default.Equals(stadt,leer)
                || EqualityComparer<T>.Default.Equals(anschluss,stadt),true,true,0)) return new List<Teil<T>>();
            return teile;
        }

        // Eine Kurskette gilt nur mit voller Deckung und denselben echten
        // Anschluss-IDs als unveraendert. 5 cm entsprechen dem Netzabgleich;
        // Public/Prefab- und Zoningseitenwechsel werden nicht weggerundet.
        public static bool Unveraendert(bool prefabGleich, bool richtungGleich,
            bool anschlussGleich, bool beidseitigVerbunden, bool seitenGleich, float kursabstand)
            => prefabGleich && richtungGleich && anschlussGleich && beidseitigVerbunden
                && seitenGleich && math.isfinite(kursabstand) && kursabstand <= .05f;

        public static bool Vollstaendig(int geplant, int erhalten, int neu, int neueSoll)
            => geplant > 0 && erhalten + neueSoll == geplant && neu == neueSoll;
    }
}
