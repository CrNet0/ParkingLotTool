using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /** Eine Quelle fuer Versuchszahl und Sperre. Erst ein gemessener Rueckweg
     *  erlaubt den naechsten Versuch; die Sitzung endet nach drei Starts. */
    public sealed class HintergrundAuftragsregel<T>
    {
        private readonly Dictionary<T, int> _versuche = new Dictionary<T, int>();
        private readonly HashSet<T> _offen = new HashSet<T>();
        public int Offen => _offen.Count;
        public bool Gesperrt(T lot) => _offen.Contains(lot);
        public int Versuche(T lot) => _versuche.TryGetValue(lot, out var n) ? n : 0;
        public bool Einreihen(T lot)
        {
            if (Versuche(lot) >= 3) return false;
            return _offen.Add(lot);
        }
        // Wiederherstellen ist kein weiterer Neubauversuch und bleibt auch
        // nach drei gescheiterten Neubauten moeglich (spaeterer Edit-Abbruch).
        public bool EinreihenRueckweg(T lot) => _offen.Add(lot);
        public int StartRueckweg(T lot)
        {
            if (!_offen.Contains(lot)) throw new InvalidOperationException("Kein offener Rueckweg.");
            return Versuche(lot);
        }
        public int Start(T lot)
        {
            if (!_offen.Contains(lot) || Versuche(lot) >= 3)
                throw new InvalidOperationException("Kein offener Bauversuch.");
            return _versuche[lot] = Versuche(lot) + 1;
        }
        public bool Wiederholen(T lot, bool rueckwegGeprueft)
            => _offen.Contains(lot) && rueckwegGeprueft && Versuche(lot) < 3;
        public void Ende(T lot) => _offen.Remove(lot);
    }

    public static class RueckwegKnotenregel
    {
        /** Eigene, abgerissene Knoten werden gemeinsam nach Lage erzeugt.
         *  Erhaltene eigene und fremde Knoten behalten ihre echte Identitaet. */
        public static bool OriginalVerwenden(bool eigen, bool abgerissen)
            => !eigen || !abgerissen;

        public static bool KursSichern(bool eigenerBesitzer, bool kante, bool geloescht, bool temp, bool erhalten)
            => eigenerBesitzer && kante && !geloescht && !temp && !erhalten;
    }

    public static class LotBesitzregel
    {
        public static bool GehoertZu<T>(T owner, T lot, Func<T,bool> istAnker, Func<T,T> parent)
            => EqualityComparer<T>.Default.Equals(owner,lot)
                || istAnker(owner) && EqualityComparer<T>.Default.Equals(parent(owner),lot);
    }

    public static class HintergrundFortschritt
    {
        public static (int Fertig, int Gesamt) Zaehle(int gesamt, int vorgemerkt, int hintergrund)
        {
            if (gesamt < 0 || vorgemerkt < 0 || hintergrund < 0)
                throw new ArgumentOutOfRangeException();
            int offen = vorgemerkt + hintergrund;
            int total = Math.Max(gesamt, offen);
            return (total - offen, total);
        }
    }
}
