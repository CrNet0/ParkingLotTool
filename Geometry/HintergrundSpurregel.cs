using System;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundSpurregel
    {
        public static bool PrivateAutos(bool auto, bool nurOeffentlich, bool verboten)
            => auto && !nurOeffentlich && !verboten;
        public static bool Einmuendung(bool hin, bool zurueck, bool reinNoetig, bool rausNoetig, int rein, int raus)
            => hin && zurueck && (!reinNoetig || rein > 0) && (!rausNoetig || raus > 0);
        // LaneSystem 3797-3830: SideConnection und ein fremdes Pfadende.
        // Ein fremder Prefabname allein waere auch eine defekte eigene Spur.
        public static bool FremdeVerbindung(bool seitenverbindung, int kantenId,
            int startId, int endeId, Func<int, bool> fremdesObjekt)
            => seitenverbindung && (startId == kantenId && fremdesObjekt(endeId)
                || endeId == kantenId && fremdesObjekt(startId));

        // LaneConnectionSystem 471-484 folgt Owner VOR Attached. Daher
        // reicht Attached am Decal allein nicht als Parkplatznachweis.
        public static T Suchbesitzer<T>(T objekt, Func<T, T> owner,
            Func<T, bool> gebaeude, Func<T, T> attached, Func<T, bool> prefab,
            Func<T, bool> lebt, T leer)
        {
            var gleich = System.Collections.Generic.EqualityComparer<T>.Default;
            T ziel = owner(objekt);
            for (int i = 0; i < 32 && lebt(ziel) && !gebaeude(ziel); i++)
            {
                T naechster = owner(ziel);
                if (gleich.Equals(naechster, leer)) break;
                if (gleich.Equals(naechster, ziel)) return leer;
                ziel = naechster;
                if (i == 31) return leer;
            }
            if (!lebt(ziel)) return leer;
            if (!gebaeude(ziel))
            {
                T eltern = attached(ziel);
                if (lebt(eltern) && prefab(eltern)) ziel = eltern;
            }
            return ziel;
        }
    }
}
