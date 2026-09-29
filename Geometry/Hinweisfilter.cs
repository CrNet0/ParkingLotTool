using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ParkingLotTool.Geometry
{
    /**
     * Welche Bauhinweise dem Nutzer begegnen - und welche nur im Bauzettel
     * stehen.
     *
     * WARUM ES DAS GIBT. Nach einem GELUNGENEN Bau standen am 2026-09-08 zwei
     * rote Fehler in der Statusleiste. Beide meldeten weggelassene Flaechen
     * von **0,00 m2**: der gestreifte Halbebenenschnitt und ein Ring, den CS2
     * ohnehin abgelehnt haette. Keiner kostet den Nutzer etwas. Warnungen, die
     * bei jedem Bau erscheinen und nichts bedeuten, machen die echten
     * unsichtbar.
     *
     * WAS ES NICHT TUT: die Meldungen unterdruecken. Sie stehen weiter im
     * Bauzettel, und `--zufahrtsschnitt` prueft ausdruecklich, dass eine
     * verworfene Scherbe im Warnungskanal auftaucht. Gefiltert wird nur die
     * Anzeige.
     *
     * WARUM IN `Geometry/`: hier liest jemand Text und entscheidet daraus,
     * was der Nutzer NICHT sieht. Verliest er sich, verschwindet im
     * schlimmsten Fall eine echte Warnung - still. Das Testprojekt uebersetzt
     * nur diesen Ordner, also gehoert es hierher.
     */
    public static class Hinweisfilter
    {
        /**
         * Ein Quadratzentimeter.
         *
         * Kleiner als jedes Merkmal des Zellenmodells - die kuerzeste von CS2
         * angenommene Kante lag in der Messung vom 2026-09-04 bei 0,499 m.
         * Darunter liegt nur Rauschen der Gleitkommazahlen.
         */
        public const double Belanglos = 1e-4;

        private static readonly Regex Flaechenangabe = new Regex(
            @"(-?[0-9]+(?:[.,][0-9]+)?(?:[eE][-+]?[0-9]+)?)\s*m2",
            RegexOptions.Compiled);

        /** Die Hinweise, die der Nutzer sehen soll. */
        public static string[] Sichtbare(IReadOnlyList<string> warnungen)
        {
            if (warnungen == null || warnungen.Count == 0)
                return System.Array.Empty<string>();
            var sichtbar = new List<string>(warnungen.Count);
            for (var i = 0; i < warnungen.Count; i++)
                if (!IstBelanglos(warnungen[i])
                    && !IstEntwicklerbefund(warnungen[i]))
                    sichtbar.Add(warnungen[i]);
            return sichtbar.ToArray();
        }

        /**
         * IST DAS EIN BEFUND UEBER UNSERE KONSTRUKTION - ALSO NICHTS FUER
         * DEN NUTZER?
         *
         * Zweite Sorte neben der Nullflaeche, und aus einem anderen Grund.
         * Die Nullflaechen sind belanglos; diese hier sind WICHTIG - nur
         * eben fuer den, der den Quelltext kennt.
         *
         * Der Nutzer am 2026-09-15 ueber die Lochtrennungsmeldung: *"Aber ist
         * das wirklich was fuer die Statusmeldung weil wir das IMMER bekommen
         * wenn ich eine ZF erstelle. Das kann den User evtl verwirren."*
         *
         * Er hat doppelt recht. Ein Alarm, der bei jedem Mal angeht, wird
         * weggelesen - und dann uebersieht man ihn, wenn er einmal wirklich
         * etwas Neues meldet. Und der Satz ist an einen Entwickler gerichtet:
         * *"that is a construction fault upstream, not a repair job"* sagt
         * jemandem ohne Quelltext nichts.
         *
         * WEG IST SIE DAMIT NICHT. Wie bei der Nullflaeche filtert nur die
         * ANZEIGE: im Bauzettel, im Baubefund des Meldereiters und im
         * Modlog steht sie unveraendert. Genau dort gehoert sie hin.
         *
         * Erkannt wird an einer festen Textstelle. Faellt die weg, erscheint
         * der Hinweis wieder in der Statusleiste - laut, nicht still. Das ist
         * dieselbe Richtung wie unten: lieber einmal zu viel zeigen.
         */
        public static bool IstEntwicklerbefund(string warnung)
            => !string.IsNullOrEmpty(warnung)
               && (warnung.Contains("hole separation")
                   // Nur mit `Auto` - das setzt der Mod nie; ein Rest aus
                   // der Pruefumgebung.
                   || warnung.Contains("automatic angle search")
                   // Aus dem toten Reparaturpass; beschreibt die Konstruktion.
                   || warnung.Contains("Engstelle"));

        /**
         * WAS IN DER HINWEISZEILE STEHT - in der Sprache des Spielers.
         *
         * Bis 2026-09-29 stand dort der Rohtext des Rechenkerns: teils nur
         * deutsch ("Randstraßen aus: 0 Autozufahrten; eine Autozufahrt
         * setzen."), teils Entwicklerenglisch ("Cell engine: 3 surface
         * ring(s) with 1.20 m2 left out - CS2 would have refused them.").
         * Der Rohtext bleibt im Bauzettel, im Log und im Meldereiter.
         *
         * Eine UNBEKANNTE Meldung erscheint unveraendert - dieselbe Richtung
         * wie beim Filter oben: lieber einmal zu viel zeigen als still eine
         * echte Warnung verschlucken.
         */
        public static (string De, string En) Anzeigetext(string warnung)
        {
            if (string.IsNullOrEmpty(warnung)) return (string.Empty, string.Empty);
            var zufahrt = Regex.Match(warnung, @"Zufahrt\s+([0-9]+)");
            var nummer = zufahrt.Success ? zufahrt.Groups[1].Value : "";
            if (warnung.Contains("0 Autozufahrten"))
                return ("Ohne Randstraßen braucht der Parkplatz eine Autozufahrt. Bitte eine setzen.",
                        "Without perimeter roads the lot needs a car entrance. Please place one.");
            if (warnung.Contains("keinen geraden Anschluss"))
                return ($"Zufahrt {nummer} findet keinen geraden Weg zu einer Fahrgasse. Zufahrt verschieben.",
                        $"Entrance {nummer} has no straight path to an aisle. Move the entrance.");
            if (warnung.Contains("trifft ein Hindernis"))
                return ($"Zufahrt {nummer} stößt auf ein Hindernis. Zufahrt verschieben.",
                        $"Entrance {nummer} runs into an obstacle. Move the entrance.");
            if (warnung.Contains("Endfussweg") || warnung.Contains("Endfußweg"))
                return ("Ein Fußweg am Ende einer Reihe passt nicht hinein und fehlt.",
                        "A footpath at the end of a row does not fit and was left out.");
            if (warnung.Contains("automatic entrances are not implemented"))
                return ("Automatische Zufahrten gibt es noch nicht - bitte von Hand setzen.",
                        "Automatic entrances are not available yet - please place them by hand.");
            if (warnung.Contains("left out") || warnung.Contains("Scherbe verworfen"))
            {
                var flaeche = GroessteFlaeche(warnung);
                var menge = flaeche > 0 ? flaeche.ToString("F1", CultureInfo.InvariantCulture) + " m²" : "";
                return ($"{(menge == "" ? "Ein Stück" : menge)} Belag fehlt: CS2 kann diese Form "
                            + "dort nicht darstellen.",
                        $"{(menge == "" ? "A piece of" : menge + " of")} surface is missing: CS2 "
                            + "cannot display this shape there.");
            }
            if (warnung.Contains("Belagvorbereitung abgebrochen")
                || warnung.Contains("Materialphase abgebrochen")
                || warnung.Contains("Materialreparatur abgebrochen"))
                return ("Die Flächen dieser Form wurden nicht vollständig fertig. Wenn etwas "
                            + "fehlt, bitte einen Vorschau-Bericht schicken.",
                        "The surfaces of this shape could not be completed. If something "
                            + "looks missing, please send a preview report.");
            return (warnung, warnung);
        }

        private static double GroessteFlaeche(string warnung)
        {
            var groesste = 0.0;
            foreach (Match treffer in Flaechenangabe.Matches(warnung))
                if (double.TryParse(treffer.Groups[1].Value.Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    groesste = System.Math.Max(groesste, System.Math.Abs(f));
            return groesste;
        }

        /**
         * Meldet dieser Hinweis nur verschwundene Nullflaeche?
         *
         * Erkannt wird an zwei festen Textstellen UND an den genannten
         * Quadratmetern. Beides muss zutreffen: eine unbekannte Meldung bleibt
         * immer sichtbar, und eine bekannte mit echter Flaeche ebenfalls.
         * Faellt eine der Textstellen weg, erscheint der Hinweis wieder -
         * laut, nicht still.
         */
        public static bool IstBelanglos(string warnung)
        {
            if (string.IsNullOrEmpty(warnung)) return false;
            var bekannt = warnung.Contains("entartete Scherbe verworfen")
                || warnung.Contains("left out");
            if (!bekannt) return false;
            return NurWinzigeFlaechen(warnung);
        }

        /**
         * Nennt die Meldung ausschliesslich Flaechen unter der Schwelle?
         *
         * Eine Zeile nennt mehrere Zahlen - etwa die Scherbe UND die
         * Rundungsgrenze. Entscheidend ist die groesste: sobald eine davon
         * spuerbar ist, bleibt der Hinweis stehen. Nennt die Zeile gar keine
         * Flaeche, gilt sie als spuerbar; raten waere hier die falsche
         * Richtung.
         */
        private static bool NurWinzigeFlaechen(string warnung)
        {
            var gefunden = false;
            foreach (Match treffer in Flaechenangabe.Matches(warnung))
            {
                gefunden = true;
                var roh = treffer.Groups[1].Value.Replace(',', '.');
                if (!double.TryParse(roh, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var flaeche))
                    return false;
                if (System.Math.Abs(flaeche) > Belanglos) return false;
            }
            return gefunden;
        }
    }
}
