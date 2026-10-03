using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /** 17:57: 2098 Buchten in einer Ausgabe. Ohne Tempo: 8 Einheiten/2 ms;
     *  im Hintergrund adaptive Grenzen aus HintergrundTempo (max. 256/16 ms).
     *  Eine Einheit bleibt unteilbar; Aufrufer liefern je Ring/Kurs/Objekt. */
    public sealed class HintergrundPortion : IDisposable
    {
        public const int MaxEinheiten = 8;
        public const double BudgetMs = 2;
        private readonly IEnumerator<int> _arbeit;
        public HintergrundTempo Tempo { get; set; }
        public bool Fertig { get; private set; }
        public int Einheiten { get; private set; }
        public int Gesamt { get; private set; }
        public HintergrundPortion(IEnumerable<int> arbeit, HintergrundTempo tempo = null)
        { _arbeit = arbeit.GetEnumerator(); Tempo = tempo; }
        public int Weiter(Func<double> millisekunden)
        {
            int ausgabe = 0; Einheiten = 0;
            double start = millisekunden();
            while (!Fertig && Einheiten < (Tempo?.Einheiten ?? MaxEinheiten)
                && (Einheiten == 0 || millisekunden() - start < (Tempo?.BudgetMs ?? BudgetMs)))
            {
                if (!_arbeit.MoveNext()) { Fertig = true; break; }
                Einheiten++; ausgabe += _arbeit.Current;
            }
            Gesamt += ausgabe;
            return ausgabe;
        }
        public void Dispose() { _arbeit.Dispose(); }
    }
}
