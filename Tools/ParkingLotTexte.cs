using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    /**
     * Texte fuer alles, was aus dem C#-Teil zum Spieler geht - Statuszeile,
     * Hinweise, Einstellungsseite, Meldungen.
     *
     * Seit 2026-10-06 stehen sie in `Lang/<sprache>.json` (siehe
     * `Geometry/Sprachtexte`). `T("schluessel", ("name", wert), ...)` holt die
     * Vorlage der eingestellten Sprache und setzt die Werte ein. Ein Text,
     * der hier im Code steht statt in der Sprachdatei, ist ein Fehler - der
     * Pruefslauf `--sprache` sucht danach.
     */
    internal static class ParkingLotTexte
    {
        internal static string T(string schluessel, params (string Name, object Wert)[] werte)
            => Sprachtexte.Text(schluessel, werte);

        /** Mehrzahl ueber `schluessel.one` / `schluessel.other`, Zahl als `{n}`. */
        internal static string TN(string schluessel, long n, params (string Name, object Wert)[] werte)
            => Sprachtexte.Anzahl(schluessel, n, werte);
    }
}
