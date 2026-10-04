using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * DIE KOLLISIONSFORM EINER LATERNE, WIE CS2 SIE GEGEN PFLANZEN PRUEFT.
     *
     * Nutzerbefund 2026-10-04: Buesche kamen je Art verschieden nah an die
     * Laterne. Nicht unser Planer, sondern CS2 blendet sie aus: Ueberschneidet
     * eine Pflanze den Koerper der Laterne, setzt `OverrideSystem` sie auf
     * Overridden (19 Klein-02-Buesche ab 0,53 m). Ein verdeckter Busch hatte in
     * der Planung aber seine Nachbarn ferngehalten - um die Laterne blieb ein Loch.
     *
     * Deshalb rechnet der Planer dieselbe Pruefung nach, ohne Vanilla oder
     * Prefabs anzufassen (Nutzer: nur per Rechenweg). Quelle:
     * Game.Objects.OverrideSystem.ObjectIterator (runde Pflanze gegen stehendes
     * Objekt) und ObjectUtils.GetStandingLegCount/GetStandingLegOffset.
     * - Pflanze: Zylinder mit Radius m_Size.x/2 bis zu ihrer Hoehe.
     * - Stehendes Objekt: je Bein ein Zylinder (CircularLeg) bzw. Kasten mit
     *   m_LegSize bis m_LegSize.y; Beine 1, 2 oder 4 bei +-m_LegOffset.
     * - Darueber (ab m_LegSize.y) der Koerper: Zylinder m_Size.x/2, wenn
     *   Circular, sonst der gedrehte Kasten der Bounds. Den trifft nur eine
     *   Pflanze, die hoeher ist als das Bein.
     * CS2 zieht ueberall 0,01 m ab; wir geben 0,05 m Reserve fuer float und
     * Gelaende dazu, in der Hoehe 0,3 m (Hang zwischen Mast und Pflanze).
     */
    public struct LaternenKoerper
    {
        public float2 Position;
        /** Lokale +Z-Achse des Modells in der Welt, Einheitsvektor. */
        public float2 Vorwaerts;
        public bool Stehend, RundesBein, BeinOhneKollision, Rund;
        /** m_LegSize */
        public float3 Bein;
        /** m_LegOffset */
        public float2 BeinVersatz;
        /** m_Bounds */
        public float3 Min, Max;
        /** m_Size.x */
        public float Groesse;

        /** Wie weit der Koerper hoechstens vom Mast reicht - fuer die Vorauswahl. */
        public float Reichweite => math.max(math.length(math.max(math.abs(Min.xz), math.abs(Max.xz))),
            math.max(Groesse * 0.5f, math.length(BeinVersatz) + math.cmax(Bein.xz) * 0.5f));
    }

    public static class Laternenkollision
    {
        private const float Rand = 0.01f, Reserve = 0.05f, HoehenReserve = 0.3f;

        /** Beruehrt eine runde Pflanze (Radius m_Size.x/2, Hoehe ueber Grund) den Koerper? */
        public static bool Beruehrt(in LaternenKoerper k, float2 p, float radius, float hoehe)
        {
            var d = p - k.Position;
            if (math.lengthsq(d) > math.square(k.Reichweite + radius + Reserve)) return false;
            var f = k.Vorwaerts;
            var rechts = new float2(f.y, -f.x); // LookRotation: rechts = oben x vorwaerts
            var q = new float2(math.dot(d, rechts), math.dot(d, f));
            var rp = radius - Rand + Reserve;
            var oben = hoehe + HoehenReserve;
            var koerperUnten = k.Min.y;
            if (k.Stehend)
            {
                var beine = 1 << ((k.BeinVersatz.x != 0f ? 1 : 0) + (k.BeinVersatz.y != 0f ? 1 : 0));
                for (var i = 0; i < beine && !k.BeinOhneKollision; i++)
                {
                    var tx = (i & 1) != 0;
                    var tz = ((k.BeinVersatz.x != 0f ? i >> 1 : i) & 1) != 0;
                    var bein = new float2(tx ? k.BeinVersatz.x : -k.BeinVersatz.x, tz ? k.BeinVersatz.y : -k.BeinVersatz.y);
                    if (k.RundesBein)
                    {
                        if (math.distance(q, bein) < rp + k.Bein.x * 0.5f - Rand) return true;
                    }
                    else if (KreisInRechteck(q, rp, bein - k.Bein.xz * 0.5f + Rand, bein + k.Bein.xz * 0.5f - Rand))
                        return true;
                }
                koerperUnten = k.Bein.y;
            }
            // Der Teil ueber dem Bein: nur, wenn die Pflanze hinaufreicht.
            if (oben - Rand <= koerperUnten + Rand || k.Max.y - Rand <= koerperUnten + Rand) return false;
            if (k.Rund) return math.length(q) < rp + k.Groesse * 0.5f - Rand;
            return KreisInRechteck(q, rp, k.Min.xz + Rand, k.Max.xz - Rand);
        }

        private static bool KreisInRechteck(float2 c, float r, float2 min, float2 max)
            => math.distancesq(c, math.clamp(c, min, max)) < r * r;
    }
}
