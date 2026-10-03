using System;

namespace ParkingLotTool.Geometry
{
    /** Mehrere Aufrufe im selben Bild zaehlen als EIN Bild; Max misst ihre
     *  Summe, nicht den groessten Einzelaufruf. */
    public sealed class HintergrundZeitmessung
    {
        public double Summe { get; private set; }
        public double Max { get; private set; }
        public int Bilder { get; private set; }
        private int _bild = -1;
        private double _bildsumme;
        public void Fuege(int bild, double ms)
        {
            if (bild != _bild) { _bild = bild; _bildsumme = 0; Bilder++; }
            _bildsumme += ms; Summe += ms; Max = Math.Max(Max,_bildsumme);
        }
    }
}
