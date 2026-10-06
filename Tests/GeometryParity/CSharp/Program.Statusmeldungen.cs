using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * `--statusmeldungen`: jede Vorschau-Ausnahme bekommt eine Ursache, die der
 * Spieler selbst beheben kann, und die Statuszeile nennt nie den rohen
 * Kerntext.
 *
 * Anlass (2026-09-29): ein Spieler las "No parking module fits inside the
 * inner contour. Ctrl+Enter writes a geometry dump." und hielt die fehlende
 * Vorschau fuer einen haengenden Zustand - seine Form war mit Randstrassen
 * schlicht zu klein.
 */
internal static partial class Program
{
    private static int PruefeStatusmeldungen()
    {
        LadeSprachdateien();
        var fehler = 0;
        void Pruefe(bool ok, string was)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER Statusmeldungen: " + was);
        }

        // Echte Ausnahme aus dem Rechenkern: ein spitzes, kleines Dreieck mit
        // Randstrassen - dieselbe Lage wie beim Spieler.
        var klein = new[] { new float2(0, 0), new float2(24, 0), new float2(4, 18) };
        var settings = LayoutSettings.Cs2;
        settings.Randstrassen = true;
        Exception geworfen = null;
        try { ParkingGeometry.Build(klein, settings); }
        catch (Exception e) { geworfen = e; }
        Pruefe(geworfen != null, "kleines Dreieck mit Randstrassen wirft keine Ausnahme");
        if (geworfen != null)
        {
            var ursache = Vorschaufehler.Einordnen(geworfen);
            Pruefe(ursache == Vorschaufehler.Ursache.ZuKlein,
                $"kleines Dreieck: Ursache {ursache}, erwartet ZuKlein "
                + $"(Meldung: {geworfen.Message})");
            var text = Vorschaufehler.Text(ursache, true);
            Pruefe(En(text).Contains("perimeter roads") && De(text).Contains("Randstraßen"),
                "mit Randstrassen fehlt der Rat, sie auszuschalten");
            Pruefe(!En(Vorschaufehler.Text(ursache, false)).Contains("perimeter"),
                "ohne Randstrassen darf der Rat sie nicht nennen");
        }

        // Die bekannten Kerntexte - Stand der throw-Stellen in Geometry/.
        var faelle = new List<(string Meldung, Vorschaufehler.Ursache Erwartet)>
        {
            ("No parking module fits inside the inner contour.", Vorschaufehler.Ursache.ZuKlein),
            ("No sub-area could be built.", Vorschaufehler.Ursache.ZuKlein),
            ("A parking module does not intersect the inner contour.", Vorschaufehler.Ursache.ZuKlein),
            ("The inset boundary leaves the site.", Vorschaufehler.Ursache.ZuKlein),
            ("The boundary inset by 13.9 m has no edge left.", Vorschaufehler.Ursache.ZuKlein),
            ("The boundary inset by 6.9 m is not counter-clockwise.", Vorschaufehler.Ursache.ZuKlein),
            ("Two adjacent boundary edges fold back on each other.", Vorschaufehler.Ursache.UmrissUngueltig),
            ("A polygon needs at least three corners.", Vorschaufehler.Ursache.UmrissUngueltig),
            ("The cell engine needs a polygon with positive area.", Vorschaufehler.Ursache.UmrissUngueltig),
            ("The site has no edge with any length.", Vorschaufehler.Ursache.UmrissUngueltig),
            ("The outer ring contains a zero-length edge.", Vorschaufehler.Ursache.UmrissUngueltig),
            ("Entrance 2: corner axis has zero length.", Vorschaufehler.Ursache.Zufahrt),
            ("Materialreparatur ueberschreitet 7,0 s in SlabFill; die Flaechen bleiben unrepariert.",
                Vorschaufehler.Ursache.Zeitgrenze),
            ("Eine Teilungsgerade braucht eine Richtung.", Vorschaufehler.Ursache.Unbekannt),
            ("", Vorschaufehler.Ursache.Unbekannt),
        };
        foreach (var (meldung, erwartet) in faelle)
        {
            var ist = Vorschaufehler.Einordnen(meldung);
            Pruefe(ist == erwartet, $"'{meldung}' -> {ist}, erwartet {erwartet}");
        }

        // Keine Statuszeile verraet Kernbegriffe oder ist leer.
        foreach (Vorschaufehler.Ursache u in Enum.GetValues(typeof(Vorschaufehler.Ursache)))
            foreach (var rand in new[] { true, false })
            {
                var s = Vorschaufehler.Text(u, rand);
                var t = (De: De(s), En: En(s));
                Pruefe(!string.IsNullOrWhiteSpace(t.De) && !string.IsNullOrWhiteSpace(t.En)
                    && !t.De.StartsWith("[") && !t.En.StartsWith("["),
                    $"{u}: leere oder fehlende Statuszeile ({s})");
                foreach (var begriff in new[] { "contour", "dump", "Teilungsgerade", "generator" })
                    Pruefe(!t.En.Contains(begriff) && !t.De.Contains(begriff),
                        $"{u}: Statuszeile enthaelt Kernbegriff '{begriff}'");
            }

        // Hinweiszeile: bekannte Kernhinweise erscheinen uebersetzt, nie roh.
        var hinweise = new[]
        {
            "Randstraßen aus: 0 Autozufahrten; eine Autozufahrt setzen.",
            "Randstraßen aus: Zufahrt 2 hat keinen geraden Anschluss; Lage ändern.",
            "Randstraßen aus: Zufahrt 3 trifft ein Hindernis.",
            "Randstrassen aus: schraeger Endfussweg an Kante 4 passt nicht in die Kontur.",
            "Randstraßen aus: Endfußweg zwischen Gassen bei 3.0/9.0 m passt nicht als gerader Streifen.",
            "Cell engine: automatic entrances are not implemented; place them by hand.",
            "Cell engine: 3 surface ring(s) with 1.20 m2 left out - CS2 would have refused them.",
            "Belagvorbereitung abgebrochen: irgendwas",
            "Teilfläche 2 blieb leer: zu schmal für den Trennweg.",
            "Teilfläche 3 blieb leer: No parking module fits inside the inner contour.",
        };
        foreach (var h in hinweise)
        {
            var a = Hinweisfilter.Anzeigetext(h);
            var t = (De: De(a.Schluessel, a.Werte), En: En(a.Schluessel, a.Werte));
            Pruefe(t.En != h && t.De != h && !t.En.StartsWith("["), $"Hinweis bleibt roh: '{h}'");
            Pruefe(!t.En.Contains("Randstra") && !t.En.Contains("Cell engine"),
                $"englischer Hinweis enthaelt Kerntext: '{t.En}'");
        }
        Pruefe(AnzeigeEn("Zufahrt 3 trifft ein Hindernis.").Contains("Entrance 3"),
            "Zufahrtsnummer geht verloren");
        Pruefe(AnzeigeEn(hinweise[8]).Contains("Sub-area 2") && AnzeigeEn(hinweise[8]).Contains("narrow"),
            "Teilflaeche zu schmal: " + AnzeigeEn(hinweise[8]));
        Pruefe(AnzeigeEn(hinweise[9]).Contains("Sub-area 3") && !AnzeigeEn(hinweise[9]).Contains("contour"),
            "Teilflaeche leer: " + AnzeigeEn(hinweise[9]));
        Pruefe(AnzeigeEn(hinweise[6]).StartsWith("1.2 m²"),
            "Flaechenangabe fehlt: " + AnzeigeEn(hinweise[6]));
        var unbekannt = "Irgendein neuer Hinweis.";
        Pruefe(AnzeigeEn(unbekannt) == unbekannt,
            "unbekannter Hinweis darf nicht verschwinden");
        Pruefe(Hinweisfilter.IstEntwicklerbefund("Cell engine: the automatic angle search is not implemented; calculated with Angle=0."),
            "Winkelsuche-Hinweis erreicht den Spieler");

        string AnzeigeEn(string h)
        {
            var a = Hinweisfilter.Anzeigetext(h);
            return En(a.Schluessel, a.Werte);
        }

        Console.WriteLine($"Statusmeldungen: {faelle.Count + 1 + hinweise.Length} Faelle, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
