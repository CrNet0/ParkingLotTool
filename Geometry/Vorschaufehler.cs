using System;

namespace ParkingLotTool.Geometry
{
    /**
     * WAS DER SPIELER LIEST, WENN DIE VORSCHAU SCHEITERT.
     *
     * Bis 2026-09-29 stand im Status der rohe Ausnahmetext des Rechenkerns:
     * "Preview aborted: No parking module fits inside the inner contour.
     * Ctrl+Enter writes a geometry dump." Ein Spieler hielt das fuer einen
     * haengenden Zustand - er wusste nicht, was die "inner contour" ist, und
     * dass seine Form mit Randstrassen schlicht zu klein war. Andere
     * Kerntexte sind deutsch ("Eine Teilungsgerade braucht eine Richtung.")
     * und erschienen so auch im englischen Spiel.
     *
     * Deshalb: jede Ausnahme wird einer Ursache zugeordnet, die der Spieler
     * selbst beheben kann, und der Status sagt, WAS er tun soll. Der rohe
     * Text bleibt im Log (`PLT-Vorschau konnte nicht berechnet werden.`).
     */
    public static class Vorschaufehler
    {
        public enum Ursache
        {
            ZuKlein,
            UmrissUngueltig,
            Zufahrt,
            Zeitgrenze,
            Unbekannt,
        }

        public static Ursache Einordnen(Exception exception)
        {
            while (exception?.InnerException != null)
                exception = exception.InnerException;
            return Einordnen(exception?.Message);
        }

        public static Ursache Einordnen(string meldung)
        {
            if (string.IsNullOrWhiteSpace(meldung)) return Ursache.Unbekannt;
            bool Hat(string teil) => meldung.IndexOf(teil,
                StringComparison.OrdinalIgnoreCase) >= 0;

            // Innenrand verschwindet oder kein Modul passt: die Form ist fuer
            // Randabstand, Randstrasse und eine Buchtreihe zu klein/zu spitz.
            if (Hat("No parking module fits") || Hat("No sub-area could be built")
                || Hat("does not intersect the inner contour")
                || Hat("The inset boundary leaves the site")
                || (Hat("The boundary inset by") && (Hat("has no edge left")
                    || Hat("is not counter-clockwise"))))
                return Ursache.ZuKlein;
            // Der gezeichnete Umriss selbst taugt nicht.
            if (Hat("fold back on each other") || Hat("at least three corners")
                || Hat("positive area") || Hat("no edge with any length")
                || Hat("contains a zero-length edge")
                || Hat("normalised site is not counter-clockwise"))
                return Ursache.UmrissUngueltig;
            if (meldung.StartsWith("Entrance", StringComparison.Ordinal)
                || Hat("Zufahrten erwarten") || Hat("Zufahrtsteilung"))
                return Ursache.Zufahrt;
            if (Hat("Materialreparatur")) return Ursache.Zeitgrenze;
            return Ursache.Unbekannt;
        }

        /** Statuszeile deutsch und englisch; der Aufrufer waehlt die Sprache. */
        public static (string De, string En) Text(Ursache ursache, bool randstrassen)
        {
            switch (ursache)
            {
                case Ursache.ZuKlein:
                    return randstrassen
                        ? ("Keine Vorschau: In diese Form passt keine Parkreihe. "
                            + "Form vergrößern oder Randstraßen ausschalten.",
                           "No preview: no parking row fits into this shape. "
                            + "Make the shape larger, or switch off the perimeter roads.")
                        : ("Keine Vorschau: In diese Form passt keine Parkreihe. "
                            + "Form vergrößern oder breiter ziehen.",
                           "No preview: no parking row fits into this shape. "
                            + "Make the shape larger or wider.");
                case Ursache.UmrissUngueltig:
                    return ("Keine Vorschau: Der Umriss kreuzt oder faltet sich. "
                            + "Eine Ecke so verschieben, dass er eine Fläche umschließt.",
                            "No preview: the outline crosses or folds back on itself. "
                            + "Move a corner so it encloses an area.");
                case Ursache.Zufahrt:
                    return ("Keine Vorschau: Eine Zufahrt passt nicht mehr an ihre Kante. "
                            + "Zufahrt verschieben oder entfernen.",
                            "No preview: an entrance no longer fits on its edge. "
                            + "Move or remove the entrance.");
                case Ursache.Zeitgrenze:
                    return ("Keine Vorschau: Die Flächen dieser Form brauchen zu lange. "
                            + "Bitte über „Report a problem“ einen Vorschau-Bericht schicken.",
                            "No preview: the surfaces of this shape take too long to "
                            + "calculate. Please send a preview report via Report a problem.");
                default:
                    return ("Keine Vorschau: Diese Form ließ sich nicht berechnen. "
                            + "Eine Ecke leicht verschieben; klappt es weiter nicht, "
                            + "bitte einen Vorschau-Bericht schicken.",
                            "No preview: this shape could not be calculated. Move a "
                            + "corner slightly; if it keeps failing, please send a "
                            + "preview report via Report a problem.");
            }
        }
    }
}
