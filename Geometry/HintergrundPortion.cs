using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /** 17:57: 2098 Buchten in einer Ausgabe. Jede Fortsetzung endet nach
     *  hoechstens 8 Arbeitseinheiten oder 2 ms; eine Einheit ist unteilbar.
     *  Aufrufer muessen deshalb bereits je Ring/Kurs/Objekt liefern. */
    public sealed class HintergrundPortion : IDisposable
    {
        public const int MaxEinheiten = 8;
        public const double BudgetMs = 2;
        private readonly IEnumerator<int> _arbeit;
        public bool Fertig { get; private set; }
        public int Einheiten { get; private set; }
        public int Gesamt { get; private set; }
        public HintergrundPortion(IEnumerable<int> arbeit) { _arbeit = arbeit.GetEnumerator(); }
        public int Weiter(Func<double> millisekunden)
        {
            int ausgabe = 0; Einheiten = 0;
            double start = millisekunden();
            while (!Fertig && Einheiten < MaxEinheiten
                && (Einheiten == 0 || millisekunden() - start < BudgetMs))
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
