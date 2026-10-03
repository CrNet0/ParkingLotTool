using System;

namespace ParkingLotTool.Geometry
{
    /** Lauf 22:03: 218 neue Kanten, ueber vier Minuten. 8 Einheiten/2 ms
     *  begrenzten auch reine Suchschritte. Entwurfsziel: unter 25 ms eigener
     *  Spielthreadarbeit; Zeitbudget bleibt unter 16 ms, Einheiten unter 256.
     *  Vanilla-Jobs und eine unteilbare Einheit brauchen eigene Ingame-Abnahme. */
    public sealed class HintergrundTempo
    {
        public int Einheiten { get; private set; } = 8;
        public double BudgetMs { get; private set; } = 2;
        public void MeldeBild(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) return;
            if (ms < 25)
            {
                Einheiten = Math.Min(256, Einheiten * 2);
                BudgetMs = Math.Min(16, BudgetMs * 1.5);
            }
            else
            {
                Einheiten = Math.Max(1, Einheiten / 2);
                BudgetMs = Math.Max(.5, BudgetMs / 2);
            }
        }
    }
}
