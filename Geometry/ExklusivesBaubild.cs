using System;

namespace ParkingLotTool.Geometry
{
    /** Zulassung vor CourseSplit: 0 Definitionen UND 0 Temps, im selben Bild.
     *  GenerateNodes liest Definition+Updated, seine NodeKey-Sammlung besitzt
     *  aber keine Auftragsgrenze (Game.dll-Messung: 2 Schluesselkollisionen).
     *  Deshalb zaehlt der Adapter auch Definitionen ohne Updated mit.
     *  Diese Regel behauptet noch keinen Nachweis der Besitzerauflösung. */
    internal static class ExklusivesBaubild
    {
        internal static bool DarfAnlegen(int definitionen, int temps, bool spielBereit)
        {
            if (definitionen < 0 || temps < 0)
                throw new ArgumentOutOfRangeException("Entityzaehler");
            return spielBereit && definitionen == 0 && temps == 0;
        }

        internal static string Wartegrund(int definitionen, int temps, bool spielBereit)
        {
            if (!spielBereit) return "Spiel wird geladen oder ist nicht im Spielmodus";
            if (definitionen != 0 || temps != 0)
                return $"{definitionen} fremde Definitionen, {temps} Temp-Entities";
            return string.Empty;
        }
    }
}
