using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Nur Kandidatenauswahl, nie Ersatz fuer den 5-cm-Abgleich. 10-cm-Zellen
    // mit acht Nachbarn finden auch Treffer beidseits der Zellgrenze.
    // 2098 Buchten im Lauf 13:01: die alte Suche pruefte je Objekt den
    // gesamten Teilebestand erneut. Jetzt wird jede Lage einmal eingeordnet.
    public sealed class HintergrundLageindex<T>
    {
        private readonly Dictionary<(long X,long Z),List<T>> _zellen = new Dictionary<(long,long),List<T>>();
        private static (long X,long Z) Zelle(float2 p) => ((long)math.floor(p.x*10f),(long)math.floor(p.y*10f));
        public void Fuege(float2 p, T wert)
        {
            if (!math.all(math.isfinite(p))) return;
            var key = Zelle(p);
            if (!_zellen.TryGetValue(key,out var liste)) _zellen.Add(key,liste = new List<T>());
            liste.Add(wert);
        }
        public IEnumerable<T> Nahe(float2 p)
        {
            if (!math.all(math.isfinite(p))) yield break;
            var key = Zelle(p);
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (_zellen.TryGetValue((key.X+x,key.Z+z),out var liste))
                        foreach (var wert in liste) yield return wert;
        }
    }
}
