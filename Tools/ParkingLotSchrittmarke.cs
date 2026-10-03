using System;
using System.IO;
using System.Text;

namespace ParkingLotTool.Tools
{
    /**
     * SCHREIBT SOFORT AUF DIE PLATTE, WO DIE MOD GERADE IST.
     *
     * Ansage des Nutzers am 2026-09-14 vor der Testveroeffentlichung: *"Es
     * waere wirklich wichtig, dass das so viel abfaengt wie moeglich, bevor es
     * ueberhaupt crasht"* - und der Grund dahinter: *"Das waere das groesste
     * Uebel, weil es Zeit kostet und wir die User nerven und evtl. nix fixen
     * koennen, weil wir gar nicht erfahren, warum der Crash kam."*
     *
     * DAS LOCH IM BISHERIGEN BERICHT. Ein Absturzbericht ist nur so gut wie
     * das Modlog, das er einpackt. Der gewoehnliche Logschreiber puffert
     * aber - und bei einem nativen Absturz ist der Prozess weg, bevor der
     * Puffer die Platte erreicht. Ausgerechnet die LETZTEN Zeilen fehlen dann,
     * also genau die, die sagen, was die Mod gerade tat.
     *
     * DESHALB DIESE SPUR. Sie haelt die letzten Schritte im Speicher und
     * schreibt bei jedem neuen Schritt die ganze Liste neu - mit
     * `Flush(true)`, das erzwingt das Durchschreiben bis auf die Platte. Nach
     * einem Absturz steht in der Datei als letzte Zeile der Schritt, in dem es
     * passiert ist.
     *
     * WAS SIE NICHT IST: kein Ersatz fuers Log und keine Messung. Hier stehen
     * grobe Schritte - "Bau: Flaechen uebergeben", "Versorgung: Leitungen
     * anlegen" -, keine Zahlen und nichts je Bild. Wer sie feiner macht,
     * macht sie langsam, und eine Spur, die bremst, schaltet irgendwann
     * jemand ab.
     */
    internal static class ParkingLotSchrittmarke
    {
        /**
         * Soviele Schritte bleiben stehen.
         *
         * Waren zwanzig. Das hat am 2026-09-14 nicht gereicht: der Abriss
         * eines mittleren Parkplatzes schreibt allein acht Zeilen, und die
         * Bauschritte davor waren beim Absturz schon herausgerutscht. Man sah
         * das Ende und nicht den Weg dorthin.
         *
         * Sechzig sind rund vier Kilobyte. Geschrieben wird weiterhin nur bei
         * einem neuen Schritt und nie je Bild.
         */
        private const int Schritte = 60;

        private static readonly string[] _ring = new string[Schritte];
        private static int _naechste;
        private static int _gesamt;
        private static readonly object _schloss = new object();
        private static string _pfad;
        private static bool _kaputt;

        internal static string Pfad
        {
            get
            {
                if (_pfad != null) return _pfad;
                _pfad = Path.Combine(
                    Path.Combine(UnityEngine.Application.persistentDataPath,
                        "Logs"),
                    "ParkingLotTool-step.log");
                return _pfad;
            }
        }

        /**
         * Haelt einen Schritt fest.
         *
         * Kurz halten: der Text landet unveraendert in der Datei, und die soll
         * man in drei Sekunden ueberblicken.
         */
        /**
         * Die Marke fuer ein SIMULATIONSSYSTEM - nur mit eingeschalteter
         * Absturzspur.
         *
         * Getrennt von `Setze`, weil die Bau- und Abrisswege ihre Marken
         * IMMER setzen sollen: sie laufen selten, und genau dort lagen die
         * bisherigen Abstuerze. Was hier durchkommt, laeuft dagegen in jedem
         * Bild, und ein erzwungener Schreibvorgang je Bild ist nichts, was
         * man jemandem unterschiebt.
         *
         * Ist der Schalter aus, kostet der Aufruf einen Feldzugriff. Deshalb
         * darf er an jeder Stelle stehen, auch in der heissesten Schleife.
         */
        internal static void Simulation(string system)
        {
            if (!Mitschreiben) return;
            Setze(system);
        }

        /**
         * Steht auf dem Schalter aus den Einstellungen. Als Feld und nicht
         * als Zugriff auf `Mod.Optionen`, damit der Aufruf oben wirklich nur
         * ein Vergleich ist - die Optionen haengen an einer Kette von
         * Eigenschaften, und die laeuft sonst sechzigmal je Sekunde mit.
         */
        internal static bool Mitschreiben { get; set; }

        // Begrenzte Absturzdiagnose: die 60 groben Schritte bleiben kurz;
        // alle 120 Bildstaende bleiben in einer eigenen, sofort gespuelten
        // Schrittspur erhalten. Anhaengen bewahrt vorherige Edit-/Apply-Laeufe.
        internal static void Versorgungsbild(string text)
        {
            try
            {
                lock (_schloss)
                {
                    var pfad = Path.Combine(Path.GetDirectoryName(Pfad),
                        "ParkingLotTool-versorgung-bilder.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(pfad));
                    using var strom = new FileStream(pfad, FileMode.Append,
                        FileAccess.Write, FileShare.ReadWrite);
                    var bytes = Encoding.UTF8.GetBytes(DateTime.Now.ToString("HH:mm:ss.fff")
                        + "  " + text + Environment.NewLine);
                    strom.Write(bytes, 0, bytes.Length);
                    strom.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn("PLT-Versorgungsbild: Schreiben fehlgeschlagen: " + ex.Message);
            }
        }

        /**
         * EINE AENDERUNG AN DER STADT AUSSERHALB DES WERKZEUGS (2026-10-04).
         *
         * Beim Discord-Bericht J54N stand als letzte Marke ein Bau, und danach
         * lief das Spiel noch Minuten - ob PLT in der Zeit etwas tat, sagte die
         * Spur nicht. Begleiter, Sync, Rettung, Waisen und Co. aendern die
         * Stadt aber auch ohne Werkzeug. Sie markieren jetzt jede solche
         * Aenderung, immer und nicht nur mit Absturzspur: das geschieht selten.
         *
         * Gegen versehentliches Fluten: dieselbe Zeile innerhalb von 30 s und
         * mehr als zehn Zeilen je Sekunde werden nur gezaehlt.
         */
        internal static void Aenderung(string text)
        {
            string zeile;
            lock (_schloss)
            {
                var jetzt = DateTime.UtcNow;
                if (text == _letzteAenderung && (jetzt - _letzteAenderungZeit).TotalSeconds < 30)
                { _unterdrueckt++; return; }
                var sekunde = jetzt.Ticks / TimeSpan.TicksPerSecond;
                if (sekunde != _aenderungsSekunde) { _aenderungsSekunde = sekunde; _aenderungenInSekunde = 0; }
                if (++_aenderungenInSekunde > 10) { _unterdrueckt++; return; }
                zeile = _unterdrueckt > 0 ? text + " (davor " + _unterdrueckt + " Zeile(n) unterdrueckt)" : text;
                _unterdrueckt = 0;
                _letzteAenderung = text;
                _letzteAenderungZeit = jetzt;
            }
            Setze(zeile);
        }

        private static string _letzteAenderung;
        private static DateTime _letzteAenderungZeit;
        private static long _aenderungsSekunde;
        private static int _aenderungenInSekunde;
        private static int _unterdrueckt;

        /**
         * LEBENSZEICHEN ALLE 10 SEKUNDEN.
         *
         * Wann das Spiel gestorben ist, stand bisher nirgends: Player.log hat
         * keine Uhrzeiten, und die Schrittspur nur die Zeit des letzten
         * Schritts. Diese Datei sagt "lief noch um ..." und in welchem Zustand.
         * Das Absturzabbild traegt zwar die genaue Zeit, fehlt aber, wenn das
         * Spiel nur haengt und hart beendet wird.
         */
        internal static string HerzschlagPfad => Path.Combine(Path.GetDirectoryName(Pfad), "ParkingLotTool-heartbeat.txt");

        private static DateTime _naechsterHerzschlag;

        internal static void Herzschlag(Func<string> zustand)
        {
            var jetzt = DateTime.UtcNow;
            if (jetzt < _naechsterHerzschlag) return;
            _naechsterHerzschlag = jetzt.AddSeconds(10);
            try
            {
                var text = "Last sign of life: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + Environment.NewLine + zustand() + Environment.NewLine;
                using var strom = new FileStream(HerzschlagPfad, FileMode.Create,
                    FileAccess.Write, FileShare.ReadWrite);
                var bytes = new UTF8Encoding(false).GetBytes(text);
                strom.Write(bytes, 0, bytes.Length);
                strom.Flush(true);
            }
            catch (Exception ausnahme)
            {
                _naechsterHerzschlag = DateTime.MaxValue;
                Mod.log.Warn("PLT-Herzschlag abgeschaltet: " + ausnahme.Message);
            }
        }

        internal static void Setze(string schritt)
        {
            if (_kaputt) return;
            try
            {
                lock (_schloss)
                {
                    _ring[_naechste] = DateTime.Now.ToString("HH:mm:ss.fff")
                        + "  " + schritt;
                    _naechste = (_naechste + 1) % Schritte;
                    _gesamt++;
                    Schreibe();
                }
            }
            catch (Exception ausnahme)
            {
                // Einmal melden, dann Ruhe. Eine Spur, die bei jedem Schritt
                // eine Warnung erzeugt, ist schlimmer als keine.
                _kaputt = true;
                Mod.log.Warn("PLT-Schrittmarke abgeschaltet: "
                    + ausnahme.Message);
            }
        }

        /**
         * Schreibt die ganze Liste neu und erzwingt das Durchschreiben.
         *
         * `Flush(true)` geht bis auf die Platte, nicht nur in den
         * Betriebssystem-Puffer. Genau darum geht es hier: was gepuffert
         * bleibt, ist nach einem nativen Absturz weg.
         */
        private static void Schreibe()
        {
            var text = new StringBuilder();
            text.AppendLine("Die letzten Schritte des Parking Lot Tool.");
            text.AppendLine("Die UNTERSTE Zeile ist die juengste. Nach einem "
                + "Absturz steht dort, was die Mod gerade tat.");
            text.AppendLine(new string('-', 70));

            var gehalten = Math.Min(_gesamt, Schritte);
            var anfang = _gesamt <= Schritte ? 0 : _naechste;
            for (var i = 0; i < gehalten; i++)
                text.AppendLine(_ring[(anfang + i) % Schritte]);

            Directory.CreateDirectory(Path.GetDirectoryName(Pfad));
            using var strom = new FileStream(Pfad, FileMode.Create,
                FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
            strom.Write(bytes, 0, bytes.Length);
            strom.Flush(true);
        }
    }
}
