using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * ZERFAELLT DAS ZONING-NETZ IN MEHRERE ZUEGE?
         *
         * Der Nutzer hat die Luecke ueber die Wasser- und Abwasseransicht
         * gefunden: Rohre laufen in den Strassen, und wo die Strasse
         * unterbrochen ist, reisst das Rohr ab. Bis dahin fiel es niemandem
         * auf, weil die Strassen unsichtbar sind.
         *
         * Diese Zeile misst es beim Bauen. Zwei Zuege sind nicht immer
         * falsch - zwei weit auseinander liegende Flaechen haben zu Recht
         * getrennte Ringe -, aber sie sind IMMER der Punkt, an dem man
         * nachsehen muss. Deshalb steht auch dabei, wo die freien Enden
         * liegen.
         */
        private static void MeldeZoningZusammenhang(
            IReadOnlyList<(float2 A, float2 B)> stuecke)
        {
            if (stuecke == null || stuecke.Count == 0) return;

            var eltern = new int[stuecke.Count];
            for (var i = 0; i < eltern.Length; i++) eltern[i] = i;
            int Wurzel(int i)
            {
                while (eltern[i] != i) i = eltern[i] = eltern[eltern[i]];
                return i;
            }
            for (var i = 0; i < stuecke.Count; i++)
            for (var k = i + 1; k < stuecke.Count; k++)
            {
                if (!ZoningStueckeBeruehren(stuecke[i], stuecke[k])) continue;
                var a = Wurzel(i);
                var b = Wurzel(k);
                if (a != b) eltern[a] = b;
            }

            var zuege = new HashSet<int>();
            for (var i = 0; i < stuecke.Count; i++) zuege.Add(Wurzel(i));
            if (zuege.Count <= 1)
            {
                Mod.log.Info($"PLT-Zoning: {stuecke.Count} Stueck(e) bilden "
                    + "EINEN zusammenhaengenden Strassenzug.");
                return;
            }

            var enden = new List<string>();
            for (var i = 0; i < stuecke.Count; i++)
            {
                foreach (var ende in new[] { stuecke[i].A, stuecke[i].B })
                {
                    var haengt = false;
                    for (var k = 0; k < stuecke.Count && !haengt; k++)
                        if (k != i)
                            haengt = ZoningPunktAufStueck(
                                stuecke[k].A, stuecke[k].B, ende);
                    if (!haengt) enden.Add($"({ende.x:F1}/{ende.y:F1})");
                }
            }
            Mod.log.Warn($"PLT-Zoning: {stuecke.Count} Stueck(e) zerfallen in "
                + $"{zuege.Count} Strassenzuege - dort reissen Wasser, "
                + "Abwasser und Strom ab. Freie Enden: "
                + (enden.Count == 0 ? "keine" : string.Join(" ", enden)));
        }

        private static bool ZoningStueckeBeruehren(
            (float2 A, float2 B) x, (float2 A, float2 B) y)
            // 01:52 meldete die Naehepruefung 1 Zug, Temp und Apply aber 2.
            // Der fertige Kursplan braucht exakt gemeinsame Endpunkte.
            => x.A.Equals(y.A) || x.A.Equals(y.B)
                || x.B.Equals(y.A) || x.B.Equals(y.B);

        private static bool ZoningPunktAufStueck(float2 a, float2 b, float2 p)
        {
            const float toleranz = 0.01f;
            var d = b - a;
            var laenge = math.length(d);
            if (laenge < toleranz) return false;
            var r = d / laenge;
            var w = p - a;
            var laengs = math.dot(w, r);
            if (laengs < -toleranz || laengs > laenge + toleranz) return false;
            return math.abs(r.x * w.y - r.y * w.x) < toleranz;
        }

        private void MeldeAblehnungen(ParkingLayout layout)
        {
            if (layout == null || layout.RejectTotals.Length == 0) return;
            var zeile = string.Empty;
            foreach (var topf in layout.RejectTotals)
            {
                var gesetzt = (int)topf.First.x;
                zeile += (zeile.Length > 0 ? " | " : "")
                    + $"{topf.Reason} {gesetzt}/{topf.Count}";
            }
            Mod.log.Info("PLT-Ablehnungen (gesetzt/versucht): " + zeile);
            if (layout.Rejects.Length == 0)
            {
                Mod.log.Info("  nichts verworfen.");
                return;
            }
            foreach (var grund in layout.Rejects)
                Mod.log.Info($"  {grund.Count,5}x  {grund.Reason}"
                    + $"   (erste bei {grund.First.x:F1}/{grund.First.y:F1})");
        }

    }
}
