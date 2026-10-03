using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundAbgleich
    {
        // Folgende Portionen verwenden fertige Knoten innerhalb von 5 cm
        // in 3D. Dieselbe XZ-Lage mit 5,1 cm Hoehenfehler bleibt getrennt.
        public static T Knoten<T>(float3 lage, IEnumerable<(T Id,float3 Lage)> kandidaten, T leer)
        {
            T ergebnis = leer; float beste = .05f;
            foreach (var k in kandidaten)
            {
                float d = math.distance(k.Lage,lage);
                if (!math.isfinite(d) || d > beste) continue;
                beste = d; ergebnis = k.Id;
            }
            return ergebnis;
        }

        // GenerateEdges uebernimmt bei Permanent vorhandene Knoten und deren Y.
        // Der bisherige XZ-Test verlor eine reine Hoehenkorrektur (0 m in XZ).
        public static float3 Anschlusslage(float3 geplant, float3 knoten, bool vorhanden)
            => vorhanden ? knoten : geplant;

        public static int ErhaltenerKnoten(float2 punkt, IEnumerable<(int Id,float3 Lage)> kandidaten)
        {
            int gefunden = -1;
            foreach (var k in kandidaten)
            {
                if (!math.all(math.isfinite(k.Lage)) || !(math.distance(punkt,k.Lage.xz) <= .05f)) continue;
                if (gefunden != -1 && gefunden != k.Id) return -2;
                gefunden = k.Id;
            }
            return gefunden;
        }

        // GroundHeightSystem 592-608 darf Aufkleber/HasBase an den NEUEN Boden
        // legen. 170/175 im Lauf 11:54:36 ist kein Beleg fuer fehlende Entities.
        // Beide Sollwerte kommen aus der Definition bzw. Vanilla, nie vom Ist-Y.
        public static bool Objektlage(float3 ist, float3 geplant, float3 boden, bool bodengebunden)
            => math.distance(ist, geplant) <= .05f
                || bodengebunden && math.distance(ist, boden) <= .05f;

        public static (float3 A, float3 B, float3 C, float3 D) Schnitt(
            (float3 A, float3 B, float3 C, float3 D) c, float von, float bis)
        {
            float3 Punkt(float t)
            {
                var ab = math.lerp(c.A,c.B,t); var bc = math.lerp(c.B,c.C,t); var cd = math.lerp(c.C,c.D,t);
                return math.lerp(math.lerp(ab,bc,t),math.lerp(bc,cd,t),t);
            }
            float3 Tangente(float t) => 3f * (math.lerp(math.lerp(c.B-c.A,c.C-c.B,t),
                math.lerp(c.C-c.B,c.D-c.C,t),t));
            var a = Punkt(von); var d = Punkt(bis);
            return (a,a+Tangente(von)*(bis-von)/3f,d-Tangente(bis)*(bis-von)/3f,d);
        }

        public static float Kurvenabstand((float3 A,float3 B,float3 C,float3 D) a,
            (float3 A,float3 B,float3 C,float3 D) b, bool innenhoeheVanilla = false)
        {
            if (!math.all(math.isfinite(a.A)) || !math.all(math.isfinite(a.B))
                || !math.all(math.isfinite(a.C)) || !math.all(math.isfinite(a.D))
                || !math.all(math.isfinite(b.A)) || !math.all(math.isfinite(b.B))
                || !math.all(math.isfinite(b.C)) || !math.all(math.isfinite(b.D))) return float.PositiveInfinity;
            return math.max(math.max(math.distance(a.A,b.A),math.distance(a.D,b.D)),
                innenhoeheVanilla ? math.max(math.distance(a.B.xz,b.B.xz),math.distance(a.C.xz,b.C.xz))
                    : math.max(math.distance(a.B,b.B),math.distance(a.C,b.C)));
        }

        public static bool Teilparameter(float2 bereich, float lage, out float t)
        {
            t = float.NaN;
            if (!math.all(math.isfinite(bereich)) || !math.isfinite(lage)
                || math.abs(bereich.y-bereich.x) <= 1e-6f
                || lage < math.cmin(bereich)-1e-5f || lage > math.cmax(bereich)+1e-5f) return false;
            t = (lage-bereich.x)/(bereich.y-bereich.x);
            return true;
        }

        // Parameterdeckung PLUS identische Zwischenknoten. Zwei nahe Sackgassen
        // ergeben keine CS2-Verbindung; umgekehrte Abschnitte sind zulaessig.
        public static bool Kette(List<(float2 Bereich, int Start, int Ende)> teile,
            out int start, out int ende)
        {
            start = ende = -1;
            if (teile.Count == 0) return false;
            var sortiert = new List<(float2 Bereich,int Start,int Ende)>();
            foreach (var p in teile)
            {
                if (!math.all(math.isfinite(p.Bereich)) || math.abs(p.Bereich.y-p.Bereich.x) <= 1e-6f) return false;
                sortiert.Add(p.Bereich.x <= p.Bereich.y ? p : (p.Bereich.yx,p.Ende,p.Start));
            }
            sortiert.Sort((a,b) => a.Bereich.x.CompareTo(b.Bereich.x));
            float erreicht = 0;
            foreach (var p in sortiert)
            {
                if (math.abs(p.Bereich.x-erreicht) > 1e-5f || p.Bereich.y > 1f+1e-5f
                    || ende != -1 && ende != p.Start) return false;
                if (start == -1) start = p.Start;
                ende = p.Ende; erreicht = p.Bereich.y;
            }
            return math.abs(erreicht-1f) <= 1e-5f;
        }
    }

    public static class HintergrundRueckwegfrist
    {
        // Ein Definitionssatz, drei Messfenster zu je 90 Bildern. Danach
        // bleibt der gespeicherte Plan gesperrt; kein erneutes Anlegen.
        public const int Fenster = 90;
        public const int MaxPruefungen = 3;
        public static bool Weiter(int abgeschlossenePruefungen) => abgeschlossenePruefungen < MaxPruefungen;
    }

    public static class HintergrundDefinitionsleben
    {
        public static bool Abschliessen(bool angemeldeterBesitzer, bool permanent, bool eigenerHilfskurs = false)
            => (angemeldeterBesitzer || eigenerHilfskurs) && permanent;
    }
}
