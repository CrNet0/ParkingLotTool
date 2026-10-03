using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /** Vollstaendige Besitzerabfrage statt nur SubNet: fehlende Pufferduplikate
     *  duerfen eigene Stage-B-Knoten nicht aus der Ruecknahme verschwinden lassen. */
    public static class HintergrundRuecknahme
    {
        public static List<T> Eigene<T>(IEnumerable<T> kandidaten, T lot, T traeger,
            Func<T,T> owner, Func<T,bool> erhalten) where T : struct
        {
            var result = new List<T>(); var gesehen = new HashSet<T>();
            foreach (var e in kandidaten)
                if (gesehen.Add(e) && !e.Equals(lot) && !e.Equals(traeger) && !erhalten(e))
                {
                    var o = owner(e);
                    if (!lot.Equals(default(T)) && o.Equals(lot)
                        || !traeger.Equals(default(T)) && o.Equals(traeger)) result.Add(e);
                }
            return result;
        }
    }
}
