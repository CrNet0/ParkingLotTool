using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    public static class HintergrundTakt
    {
        // Lauf 13:01: Starts 10,792/12,779/19,000 unmittelbar nach dem
        // Abschluss. Entwurfswerte, keine behauptete FPS-Abnahme: 30 freie
        // Bilder zwischen Lots, 120 vor Wiederholung, Abnahme alle 8 Bilder.
        public const int Auftragspause = 30, Versuchspause = 120, Pruefabstand = 8;
        public static bool Faellig(int bild, int fruehestens) => bild >= fruehestens;
        public static IReadOnlyList<T> Liste<T>(IReadOnlyList<T> optional)
            => optional ?? Array.Empty<T>();
        public static bool WerkzeugdefinitionBeenden(bool eigen, bool permanent, bool entwurf)
            => eigen && !permanent && !entwurf;
    }
}
