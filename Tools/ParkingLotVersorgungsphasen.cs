using Game;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotVersorgungsphasen
    {
        internal static void Registriere(UpdateSystem system)
        {
            // Dekompilat SystemOrder:108/109: GraphDelete laeuft in Phase 1.
            // Phase 2 verfehlte beide Loeschabfragen: im letzten Absturzlog
            // verschwanden 3 Leitungen und 5 Knoten ohne Flussgraph-Nachweis.
            // Deleted muss vor BEIDEN GraphDelete-Systemen sichtbar sein.
            // Einmal registrieren; zwei UpdateBefore<T,U>-Aufrufe wuerden
            // denselben Abriss zweimal je Bild ausfuehren (UpdateSystem).
            system.UpdateBefore<ParkingLotLeitungsabrissSystem>(SystemUpdatePhase.Modification1);
        }
    }
}
