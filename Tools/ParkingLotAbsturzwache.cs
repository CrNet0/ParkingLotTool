using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * MERKT SICH, OB DIE LETZTE SITZUNG ABGESTUERZT IST.
     *
     * Ansage des Nutzers am 2026-09-14: *"Wir brauchen auch noch am besten
     * einen 'Absturz'-Debug-Auswurf im Reiter Debug fuer den User."*
     *
     * WARUM DAS NICHT WIE DIE ANDEREN KNOEPFE GEHT. Stuerzt CS2 nativ ab, ist
     * das Spiel im selben Moment weg - `Managed Stacktrace` bleibt bei dieser
     * Absturzsorte sogar leer. Es kann also niemand mehr etwas anklicken. Der
     * einzige Weg fuehrt ueber den NAECHSTEN Start.
     *
     * DIE MARKE. Beim Laden legt die Mod eine Datei an und loescht sie beim
     * ordentlichen Beenden wieder. Liegt sie beim Start noch da, hat die
     * vorige Sitzung nicht sauber aufgehoert. Das ist absichtlich grob: ein
     * abgewuergter Prozess und ein echter Absturz sehen gleich aus, und lieber
     * einmal zu viel gefragt als einen Absturz verschwiegen.
     *
     * DIE BESTAETIGUNG kommt aus `Player-prev.log`. Dort steht die Meldung des
     * VORIGEN Laufs - bei unseren bisherigen Abstuerzen immer dieselbe:
     * `UpdateFrame added to unsupported type`, gefolgt von
     * `Native Crash Reporting`. Steht sie da, ist es wirklich ein Absturz
     * gewesen und kein Alt+F4.
     */
    internal static class ParkingLotAbsturzwache
    {
        private const string MarkenName = "ParkingLotTool-session.marker";

        /** Steht nach `Pruefe` fest und wird von der UI abgefragt. */
        internal static bool LetzteSitzungAbgestuerzt { get; private set; }

        /**
         * Was im vorigen Player.log gefunden wurde - fuer die Anzeige.
         *
         * Als Funktion, nicht als Text: `Pruefe` laeuft ganz am Anfang von
         * `OnLoad`, noch vor den Sprachdateien, und die Anzeige soll auch einem
         * spaeteren Sprachwechsel folgen. Erfasst werden hier nur die Fakten;
         * uebersetzt wird beim Abfragen. Das Log bekommt seinen deutschen Text
         * gleich in `Pruefe`.
         */
        internal static Func<string> Befund { get; private set; } = () => string.Empty;

        private static string Ordner =>
            Path.Combine(Application.persistentDataPath, "Logs");

        /**
         * Beim Laden aufrufen: liest die Marke, stellt den Befund fest und
         * legt die Marke fuer DIESE Sitzung neu an.
         */
        internal static void Pruefe()
        {
            try
            {
                var marke = Path.Combine(Ordner, MarkenName);
                var offen = File.Exists(marke);
                if (offen)
                {
                    LetzteSitzungAbgestuerzt = true;
                    var sitzungsstart = LiesStart(marke);
                    var log = LiesVorigesLog();
                    var abbild = ParkingLotAbsturzabbild.JuengsterAbsturzSeit(sitzungsstart);
                    Befund = () => log.Anzeige()
                        + (abbild != null ? " " + ParkingLotAbsturzabbild.Kurz(abbild) : string.Empty);
                    Rette(sitzungsstart);
                    Mod.log.Warn("PLT-Absturzwache: die vorige Sitzung hat "
                        + "nicht ordentlich aufgehoert. " + log.Logtext()
                        + (abbild != null ? " " + ParkingLotAbsturzabbild.KurzFuersLog(abbild) : string.Empty)
                        + " Im Melde-Reiter steht jetzt ein Knopf fuer den "
                        + "Absturzbericht.");
                    SchnuereVonSelbst();
                }

                Directory.CreateDirectory(Ordner);
                File.WriteAllText(marke,
                    "Diese Datei sagt nur: PLT laeuft gerade." + Environment.NewLine
                    + "Beim ordentlichen Beenden loescht die Mod sie wieder."
                    + Environment.NewLine + "Liegt sie beim Start noch da, "
                    + "ist die vorige Sitzung abgestuerzt." + Environment.NewLine
                    + "Gestartet: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzwache nicht moeglich: "
                    + ausnahme.Message);
            }
        }

        /**
         * SCHNUERT DAS PAKET SOFORT, OHNE DASS JEMAND KLICKT.
         *
         * Der Knopf im Melde-Reiter setzt voraus, dass der Spieler wieder in
         * seinen Spielstand kommt. Am 2026-09-22 war genau das der Fall
         * nicht: der Stand stuerzte beim Spielen ab, und danach kam der
         * Tester nicht mehr hinein - also auch nicht an den Knopf. Der
         * Bericht, der den Absturz erklaert haette, war damit unerreichbar.
         *
         * Das Paket braucht den Spielstand aber gar nicht. Es liest Dateien:
         * die geretteten Spuren, `Player-prev.log`, das Modlog-Ende und die
         * Umgebung. Alles davon steht beim Start schon auf der Platte.
         *
         * Also wird es hier geschnuert, in dem Moment, in dem die Wache den
         * Absturz erkennt. Der Knopf bleibt - wer hineinkommt, kann weiter
         * einen frischen Bericht mit Bauzettel erzeugen. Dieser hier ist der
         * Bericht fuer den Fall, dass es nicht geht.
         *
         * Ein Fehlschlag darf das Laden nicht aufhalten: schlimmstenfalls
         * fehlt eine Datei, die es vorher auch nicht gab.
         */
        private static void SchnuereVonSelbst()
        {
            try
            {
                var pfad = ParkingLotMeldepaket.Schnuere(
                    ParkingLotMeldepaket.Anlass.Absturz, out var grund);
                if (pfad != null)
                {
                    AutoBericht = pfad;
                    Mod.log.Warn("PLT-Absturzbericht AUTOMATISCH erstellt, "
                        + "ohne dass etwas angeklickt werden muss: " + pfad
                        + " - diese eine Datei genuegt fuer die Meldung. Sie "
                        + "enthaelt keine Pfade und keine Kennungen.");
                }
                else
                {
                    Mod.log.Warn("PLT-Absturzbericht konnte nicht automatisch "
                        + "erstellt werden: " + (grund ?? "kein Grund genannt")
                        + " Der Knopf im Melde-Reiter geht weiter.");
                }
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzbericht konnte nicht automatisch "
                    + "erstellt werden: " + ausnahme.Message);
            }
        }

        /** Pfad des beim Start selbst erstellten Berichts, sonst leer. */
        internal static string AutoBericht { get; private set; }
            = string.Empty;

        /** Beim ordentlichen Beenden aufrufen. */
        internal static void Beende()
        {
            try
            {
                var marke = Path.Combine(Ordner, MarkenName);
                if (File.Exists(marke)) File.Delete(marke);
            }
            catch
            {
                // Bleibt die Marke liegen, fragt die Mod beim naechsten Start
                // einmal zu viel. Das ist der harmlose Ausgang.
            }
        }

        /**
         * Sucht im Player.log des vorigen Laufs nach der Absturzmeldung.
         *
         * `Player-prev.log` ist der vorige Lauf - genau der, der abgestuerzt
         * ist. Das aktuelle `Player.log` gehoert schon zu dieser Sitzung.
         */
        /** Was im vorigen Player.log stand - Fakten, zwei Darstellungen. */
        private sealed class VorigesLog
        {
            internal bool Fehlt, Unlesbar, Nativ, MonoAssertion, UpdateFrame;
            internal string Grund;

            internal bool KeineMeldung => !Nativ && !MonoAssertion && !UpdateFrame;

            /** Fuer den Melde-Reiter, in der Sprache des Spielers. */
            internal string Anzeige()
            {
                if (Fehlt) return ParkingLotTexte.T("absturz.keinVorigesLog");
                if (Unlesbar) return ParkingLotTexte.T("absturz.nichtLesbar");
                if (KeineMeldung) return ParkingLotTexte.T("absturz.keineMeldung");
                var teile = new System.Collections.Generic.List<string>();
                if (Nativ) teile.Add(ParkingLotTexte.T("absturz.teil.nativ"));
                if (MonoAssertion) teile.Add(ParkingLotTexte.T("absturz.teil.mono"));
                if (UpdateFrame) teile.Add(ParkingLotTexte.T("absturz.teil.updateFrame"));
                return ParkingLotTexte.T("absturz.steht",
                    ("liste", string.Join(ParkingLotTexte.T("absturz.und"), teile)));
            }

            /** Fuers Log, deutsch wie alle Logzeilen. */
            internal string Logtext()
            {
                if (Fehlt) return "Ein vorheriges Player.log gibt es nicht.";
                if (Unlesbar) return "Das vorige Player.log war nicht lesbar (" + Grund + ").";
                if (KeineMeldung)
                    return "Im vorigen Player.log steht keine Absturzmeldung - "
                        + "moeglicherweise wurde das Spiel nur hart beendet.";
                var teile = new System.Collections.Generic.List<string>();
                if (Nativ) teile.Add("nativer Absturz");
                if (MonoAssertion) teile.Add("Mono-Assertion bei generischer Reflection");
                if (UpdateFrame) teile.Add("\"UpdateFrame added to unsupported type\"");
                return "Im vorigen Player.log steht: " + string.Join(" und ", teile) + ".";
            }
        }

        /**
         * Sucht im Player.log des vorigen Laufs nach der Absturzmeldung.
         *
         * `Player-prev.log` ist der vorige Lauf - genau der, der abgestuerzt
         * ist. Das aktuelle `Player.log` gehoert schon zu dieser Sitzung.
         */
        private static VorigesLog LiesVorigesLog()
        {
            var aus = new VorigesLog();
            try
            {
                var pfad = Path.Combine(
                    Directory.GetParent(Ordner)?.FullName ?? Ordner,
                    "Player-prev.log");
                if (!File.Exists(pfad)) { aus.Fehlt = true; return aus; }

                using var strom = new FileStream(pfad, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite);
                using var leser = new StreamReader(strom);
                var text = leser.ReadToEnd();

                aus.Nativ = text.Contains("Native Crash Reporting");
                aus.MonoAssertion = text.Contains("* Assertion at ")
                    && text.Contains("reflection_bind_generic_method_parameters");
                aus.UpdateFrame = text.Contains(
                    "UpdateFrame added to unsupported type");
            }
            catch (Exception ausnahme)
            {
                aus.Unlesbar = true;
                aus.Grund = ausnahme.Message;
            }
            return aus;
        }

        /** Vorsilbe der geretteten Dateien. Eigene Sorte fuer die Logpflege. */
        private const string Rettung = "ParkingLotTool-crashstate-";

        /**
         * RETTET DIE SPUREN DES ABGESTUERZTEN LAUFS, BEVOR SIE UEBERSCHRIEBEN
         * WERDEN.
         *
         * Ohne diesen Schritt waere der Absturzbericht fast wertlos, und das
         * faellt erst auf, wenn man ihn braucht:
         *
         *   - `ParkingLotTool-step.log` wird beim ERSTEN Schritt dieser
         *     Sitzung ueberschrieben - also genau dann, wenn der Nutzer das
         *     Spiel neu startet, um den Bericht zu erzeugen.
         *   - `ParkingLotTool.Mod.log` rotiert NICHT. Es gibt nur eine Datei,
         *     und CS2 faengt beim Start von vorn an. Das Log des abgestuerzten
         *     Laufs ist zu diesem Zeitpunkt bereits verloren - dagegen kann
         *     die Mod nichts tun, es gehoert ihr nicht. Genau deshalb ist die
         *     Schrittspur so wichtig: sie gehoert uns.
         *   - die juengsten Abzuege stammen aus dem abgestuerzten Lauf - aber
         *     nur, solange der Nutzer nichts Neues baut. Ein einziger Bau
         *     nach dem Neustart, und `Juengste` liefert den falschen.
         *
         * Kopiert wird deshalb sofort und unter eigenem Namen. Was hier liegt,
         * gehoert zum Absturz und kann von nichts mehr verdraengt werden.
         */
        /** Startzeit der abgestuerzten Sitzung aus ihrer Marke. */
        private static DateTime LiesStart(string marke)
        {
            try
            {
                foreach (var zeile in File.ReadAllLines(marke))
                    if (zeile.StartsWith("Gestartet: ", StringComparison.Ordinal)
                        && DateTime.TryParseExact(zeile.Substring(11).Trim(), "yyyy-MM-dd HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var start))
                        return start;
                return File.GetLastWriteTime(marke);
            }
            catch { return DateTime.MinValue; }
        }

        /**
         * DAS ABSTURZABBILD UND DAS LETZTE LEBENSZEICHEN DAZU (2026-10-04).
         *
         * Beides gehoert zum abgestuerzten Lauf und kommt deshalb hier unter
         * eigenem Stempel in die Rettung - das Lebenszeichen ueberschreibt
         * die neue Sitzung nach zehn Sekunden.
         */
        private static int RetteAbbild(DateTime sitzungsstart, string stempel)
        {
            var gerettet = 0;
            var herz = ParkingLotSchrittmarke.HerzschlagPfad;
            var lebenszeichen = File.Exists(herz) ? File.ReadAllText(herz).Trim() : null;
            if (lebenszeichen != null)
            {
                File.Copy(herz, Path.Combine(Ordner, Rettung + stempel + "-heartbeat.txt"), true);
                gerettet++;
            }
            var abbilder = ParkingLotAbsturzabbild.Juengste(3)
                .Where(p => File.GetLastWriteTime(p) >= sitzungsstart.AddSeconds(-5)).ToList();
            var kopf = "Crashed session started " + sitzungsstart.ToString("yyyy-MM-dd HH:mm:ss")
                + Environment.NewLine + (lebenszeichen ?? "No sign of life recorded.");
            File.WriteAllText(Path.Combine(Ordner, Rettung + stempel + "-crashdump.txt"),
                ParkingLotAbsturzabbild.Bericht(abbilder, kopf));
            return gerettet + 1;
        }

        private static void Rette(DateTime sitzungsstart)
        {
            var gerettet = 0;
            /*
             * JEDER ABSTURZ BEKOMMT SEINEN EIGENEN STEMPEL.
             *
             * Der Name war bis zum 2026-09-14 fest. Zwei Abstuerze kurz
             * hintereinander, und der zweite hat die Spur des ersten
             * ueberschrieben - beim Bulldozer-Absturz an diesem Abend genau
             * so geschehen. Eine geloeschte Spur ist schlimmer als keine, weil
             * man sie fuer vorhanden haelt.
             */
            var stempel = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                var spur = Path.Combine(Ordner, "ParkingLotTool-step.log");
                if (File.Exists(spur))
                {
                    File.Copy(spur, Path.Combine(Ordner,
                        Rettung + stempel + "-steps.log"), true);
                    gerettet++;
                }

                foreach (var sorte in new[] { "prebuild", "debug", "summary" })
                {
                    var juengste = Directory
                        .GetFiles(Ordner, "ParkingLotTool-" + sorte + "-*")
                        .Where(pfad => !pfad.Contains("-latest")
                            && !pfad.Contains(Rettung))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (juengste == null) continue;
                    File.Copy(juengste, Path.Combine(Ordner,
                        Rettung + stempel + "-" + Path.GetFileName(juengste)),
                        true);
                    gerettet++;
                }

                try { gerettet += RetteAbbild(sitzungsstart, stempel); }
                catch (Exception ausnahme)
                {
                    Mod.log.Warn("PLT-Absturzwache: Absturzabbild nicht auswertbar: " + ausnahme.Message);
                }

                Mod.log.Warn($"PLT-Absturzwache: {gerettet} Datei(en) des "
                    + "abgestuerzten Laufs gerettet. Das Modlog selbst ist "
                    + "nicht zu retten - es rotiert nicht und war beim Laden "
                    + "schon neu.");
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzwache konnte die Spuren nicht "
                    + "retten: " + ausnahme.Message);
            }
        }

        /**
         * Die Dateien, die nur in einen ABSTURZBERICHT gehoeren.
         *
         * Das Player.log des vorigen Laufs traegt die Absturzmeldung, unser
         * eigenes Log von damals den Weg dorthin. Beides liegt beim naechsten
         * Start schon als `-prev` daneben - wer erst beim Melden danach sucht,
         * findet es nicht mehr.
         */
        internal static string[] Absturzdateien()
        {
            try
            {
                var spiel = Directory.GetParent(Ordner)?.FullName ?? Ordner;
                var fest = new[]
                {
                    Path.Combine(spiel, "Player-prev.log"),
                    Path.Combine(spiel, "Player.log"),
                };
                // Die geretteten Dateien zuerst - sie sind der eigentliche
                // Inhalt eines Absturzberichts.
                return Directory.GetFiles(Ordner, Rettung + "*")
                    .Concat(fest)
                    .Where(File.Exists)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
