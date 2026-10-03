using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * LIEST DAS ABSTURZABBILD, DAS CS2 BEI JEDEM NATIVEN ABSTURZ SELBST
     * SCHREIBT (2026-10-04).
     *
     * Anlass: der Discord-Bericht J54N. Im Player.log stand nur "Native Crash
     * Reporting" mit leerem "Managed Stacktrace", kein Modlog, keine Spur.
     * Leer heisst: der Absturz war nicht im Hauptthread, sondern in einem
     * Job-Thread. Was dort geschah, steht nur im Abbild, das CS2 ueber
     * Crashpad (Backtrace) nach `.cache/backtrace/crashpad/reports/*.dmp`
     * legt - auf dem Rechner des Spielers, auch wenn das Hochladen scheitert.
     *
     * Hier wird daraus herausgelesen, was zum Einordnen reicht: wann, welche
     * Fehlerart, welcher Thread, an welcher Stelle, und welche Burst-Jobs auf
     * dem Stapel des Threads lagen. Burst-Funktionen tragen in
     * `lib_burst_generated.dll` nur Hash-Namen; `ParkingLotBurstjobs` macht
     * daraus Jobnamen, sobald die Tabelle gemessen ist. Auch ohne Namen ist
     * der Hash ein Fingerabdruck: zwei Abstuerze mit demselben Hash sind
     * dieselbe Stelle.
     *
     * Ins Paket kommt NUR dieser Text - keine Speicherinhalte, keine Pfade.
     * Das Abbild selbst ist 30-40 MB gross und enthaelt Speicher des Spiels.
     *
     * Aufbau des Formats: Microsoft Minidump (MINIDUMP_HEADER, Streams 3
     * Threads, 4 Module, 6 Ausnahme, 24 Threadnamen); x64-CONTEXT mit Rsp bei
     * 0x98 und Rip bei 0xF8. Gegengeprueft an fuenf Abstuerzen des Nutzers
     * vom 2026-10-03.
     */
    internal static class ParkingLotAbsturzabbild
    {
        internal static string Ordner => Path.Combine(
            Application.persistentDataPath, ".cache", "backtrace", "crashpad",
            "reports");

        /** Ausnahmecode, mit dem Backtrace ein Abbild OHNE Absturz anfordert. */
        private const uint AngefordertesAbbild = 0x0517A7ED;

        internal sealed class Befund
        {
            internal DateTime Zeit;
            internal uint Code;
            internal ulong[] Parameter = Array.Empty<ulong>();
            internal string Thread = "?";
            internal string Stelle = "?";
            internal string StellenJob;
            internal readonly List<string> Jobs = new List<string>();
            internal readonly List<string> Stapel = new List<string>();
            internal string Fehler;
            internal bool IstAbsturz => Fehler == null && Code != AngefordertesAbbild;
        }

        /** Die juengsten Abbilder, neuestes zuerst. */
        internal static List<string> Juengste(int anzahl)
        {
            try
            {
                if (!Directory.Exists(Ordner)) return new List<string>();
                return new DirectoryInfo(Ordner).GetFiles("*.dmp")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(anzahl).Select(f => f.FullName).ToList();
            }
            catch { return new List<string>(); }
        }

        /** Das juengste echte Absturzabbild ab `seit`, sonst null. */
        internal static Befund JuengsterAbsturzSeit(DateTime seit)
        {
            foreach (var pfad in Juengste(5))
            {
                if (File.GetLastWriteTime(pfad) < seit) break;
                var befund = Lies(pfad);
                if (befund.IstAbsturz && befund.Zeit >= seit.AddSeconds(-5))
                    return befund;
            }
            return null;
        }

        /** Eine Zeile fuer den Melde-Reiter, auf Deutsch wie der Befund dort. */
        internal static string Kurz(Befund b)
        {
            if (b == null) return null;
            var job = b.Jobs.FirstOrDefault() ?? b.StellenJob;
            return "Absturzabbild " + b.Zeit.ToString("HH:mm:ss") + ": "
                + Fehlerart(b, deutsch: true) + " im Thread " + b.Thread
                + (job != null ? ", Burst-Job " + Jobtext(job) : "") + ".";
        }

        /** Der Text fuers Meldepaket, englisch wie alles im Paket. */
        internal static string Bericht(IEnumerable<string> pfade, string kopf = null)
        {
            var text = new StringBuilder();
            text.AppendLine("Parking Lot Tool - crash dumps written by the game (Crashpad)");
            text.AppendLine(new string('-', 70));
            if (kopf != null) text.AppendLine(kopf);
            var zahl = 0;
            foreach (var pfad in pfade)
            {
                zahl++;
                var b = Lies(pfad);
                text.AppendLine();
                if (b.Fehler != null)
                {
                    text.AppendLine("Dump " + File.GetLastWriteTime(pfad).ToString("yyyy-MM-dd HH:mm:ss")
                        + ": not readable (" + b.Fehler + ")");
                    continue;
                }
                text.AppendLine("Dump " + b.Zeit.ToString("yyyy-MM-dd HH:mm:ss") + " (local time)");
                if (!b.IstAbsturz)
                {
                    text.AppendLine("  Not a crash: the game's error reporter wrote this dump on request.");
                    continue;
                }
                text.AppendLine("  What:   " + Fehlerart(b, deutsch: false));
                text.AppendLine("  Thread: " + b.Thread + Threadart(b.Thread));
                text.AppendLine("  Where:  " + b.Stelle);
                if (b.StellenJob != null)
                    text.AppendLine("  Nearest job export to the crash address (can be a neighbouring function): " + Jobtext(b.StellenJob));
                if (b.Jobs.Count > 0)
                    text.AppendLine("  Jobs entered on this thread (innermost first, the reliable hint): "
                        + string.Join(", ", b.Jobs.Select(Jobtext)));
                if (b.Stapel.Count > 0)
                {
                    text.AppendLine("  Code addresses on the stack (scan, innermost first, may contain stale entries):");
                    foreach (var zeile in b.Stapel) text.AppendLine("    " + zeile);
                }
            }
            if (zahl == 0)
                text.AppendLine("No crash dump found. Either the game did not crash natively, or its "
                    + "error reporter was switched off.");
            var tabelle = ParkingLotBurstjobs.Stand;
            text.AppendLine();
            text.AppendLine("Job name table: " + tabelle);
            return text.ToString();
        }

        private static string Jobtext(string hash)
        {
            var name = ParkingLotBurstjobs.Name(hash);
            return name != null ? hash.Substring(0, 8) + " = " + name : hash;
        }

        private static string Threadart(string thread)
        {
            if (thread.StartsWith("Job.Worker", StringComparison.Ordinal))
                return " (job system worker: a Burst job crashed, so Player.log has no managed stack)";
            return "";
        }

        private static string Fehlerart(Befund b, bool deutsch)
        {
            switch (b.Code)
            {
                case 0xC0000005:
                    {
                        var art = b.Parameter.Length > 0 ? b.Parameter[0] : 0;
                        var adresse = b.Parameter.Length > 1 ? b.Parameter[1] : 0;
                        var nullzeiger = adresse < 0x10000;
                        if (deutsch)
                            return (art == 1 ? "Schreiben" : art == 8 ? "Ausfuehren" : "Lesen")
                                + " an 0x" + adresse.ToString("X")
                                + (nullzeiger ? " (Nullzeiger, typisch fuer eine geloeschte Entity)" : "");
                        return "access violation, "
                            + (art == 1 ? "write" : art == 8 ? "execute" : "read")
                            + " at 0x" + adresse.ToString("X")
                            + (nullzeiger ? " (null pointer + offset, typical for a deleted entity "
                                + "or a missing component)" : "");
                    }
                case 0xC00000FD: return deutsch ? "Stapelueberlauf" : "stack overflow";
                case 0xC0000374: return deutsch ? "Heap beschaedigt" : "heap corruption";
                case 0xC0000409: return deutsch ? "Sofortabbruch (fail fast)" : "fail fast / stack buffer overrun";
                case 0x80000003: return deutsch ? "Haltepunkt" : "breakpoint";
                case 0xE06D7363: return deutsch ? "C++-Ausnahme" : "C++ exception";
                default: return (deutsch ? "Ausnahme 0x" : "exception 0x") + b.Code.ToString("X8");
            }
        }

        private sealed class Modul
        {
            internal ulong Basis;
            internal uint Groesse;
            internal string Name;
            internal string Pfad;
        }

        internal static Befund Lies(string pfad)
        {
            var b = new Befund();
            try
            {
                using var strom = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new BinaryReader(strom);
                uint U32(long o) { strom.Position = o; return r.ReadUInt32(); }
                ulong U64(long o) { strom.Position = o; return r.ReadUInt64(); }
                string Str(long rva)
                {
                    var laenge = (int)U32(rva);
                    if (laenge <= 0 || laenge > 4096) return "";
                    return Encoding.Unicode.GetString(r.ReadBytes(laenge));
                }

                if (U32(0) != 0x504D444D) { b.Fehler = "not a minidump"; return b; }
                b.Zeit = DateTimeOffset.FromUnixTimeSeconds(U32(20)).LocalDateTime;
                var anzahl = U32(8);
                var verzeichnis = U32(12);
                var streams = new Dictionary<uint, uint>();
                for (var i = 0; i < anzahl; i++)
                {
                    var o = verzeichnis + i * 12L;
                    streams[U32(o)] = U32(o + 8);
                }

                var module = new List<Modul>();
                if (streams.TryGetValue(4, out var ml))
                {
                    var n = U32(ml);
                    for (var i = 0; i < n && i < 4096; i++)
                    {
                        var o = ml + 4 + i * 108L;
                        var voll = Str(U32(o + 20));
                        module.Add(new Modul
                        {
                            Basis = U64(o),
                            Groesse = U32(o + 8),
                            Pfad = voll,
                            Name = Path.GetFileName(voll),
                        });
                    }
                }
                module.Sort((x, y) => x.Basis.CompareTo(y.Basis));

                var namen = new Dictionary<uint, string>();
                if (streams.TryGetValue(24, out var tn))
                {
                    var n = U32(tn);
                    for (var i = 0; i < n && i < 4096; i++)
                    {
                        var o = tn + 4 + i * 12L;
                        var id = U32(o);
                        namen[id] = Str((long)U64(o + 4));
                    }
                }

                if (!streams.TryGetValue(6, out var ex)) { b.Fehler = "no exception record"; return b; }
                var tid = U32(ex);
                b.Code = U32(ex + 8);
                var adresse = U64(ex + 24);
                var nPar = Math.Min(U32(ex + 32), 15u);
                b.Parameter = new ulong[nPar];
                for (var i = 0; i < nPar; i++) b.Parameter[i] = U64(ex + 40 + i * 8L);
                var kontext = U32(ex + 164);
                var rsp = U64(kontext + 0x98);
                b.Thread = namen.TryGetValue(tid, out var nm) && nm.Length > 0 ? nm : "#" + tid;
                if (!b.IstAbsturz) return b;

                var burst = module.FirstOrDefault(m => m.Name.StartsWith("lib_burst_generated", StringComparison.OrdinalIgnoreCase));
                var exporte = burst != null ? Exporte(burst.Pfad) : null;

                Modul Finde(ulong a)
                {
                    int lo = 0, hi = module.Count - 1;
                    while (lo <= hi)
                    {
                        var mitte = (lo + hi) / 2;
                        var m = module[mitte];
                        if (a < m.Basis) hi = mitte - 1;
                        else if (a >= m.Basis + m.Groesse) lo = mitte + 1;
                        else return m;
                    }
                    return null;
                }
                string Ort(ulong a, out string hash, out bool einsprung)
                {
                    hash = null;
                    einsprung = false;
                    var m = Finde(a);
                    if (m == null) return null;
                    var versatz = a - m.Basis;
                    var text = m.Name + "+0x" + versatz.ToString("X");
                    if (m == burst && exporte != null
                        && exporte.Naechster((uint)versatz, out var export, out var abstand))
                    {
                        var treffer = HashMuster.Match(export);
                        if (treffer.Success)
                        {
                            hash = treffer.Value;
                            // Ein Export, der NUR der Hash ist, ist der
                            // Einsprung eines Jobs; knapp dahinter liegt die
                            // Ruecksprungadresse in diesen Einsprung.
                            einsprung = export == hash && abstand < 0x100;
                            text += " (job " + hash.Substring(0, 8) + ")";
                        }
                    }
                    return text;
                }

                b.Stelle = Ort(adresse, out var stellenHash, out _) ?? "0x" + adresse.ToString("X") + " (no module: generated or JIT code)";
                b.StellenJob = stellenHash;

                // Stapel des abgestuerzten Threads ab Rsp absuchen.
                if (streams.TryGetValue(3, out var tl))
                {
                    var n = U32(tl);
                    for (var i = 0; i < n && i < 8192; i++)
                    {
                        var o = tl + 4 + i * 48L;
                        if (U32(o) != tid) continue;
                        var start = U64(o + 24);
                        var groesse = U32(o + 32);
                        var rva = U32(o + 36);
                        var ab = rsp >= start && rsp < start + groesse ? (long)(rsp - start) : 0;
                        var laenge = (int)Math.Min(groesse - ab, 0x10000);
                        strom.Position = rva + ab;
                        var stapel = r.ReadBytes(laenge);
                        var gesehen = new HashSet<string>();
                        for (var k = 0; k + 8 <= stapel.Length && b.Stapel.Count < 16; k += 8)
                        {
                            var wert = BitConverter.ToUInt64(stapel, k);
                            var ort = Ort(wert, out var hash, out var einsprung);
                            if (ort == null) continue;
                            if (einsprung && !b.Jobs.Contains(hash)) b.Jobs.Add(hash);
                            if (gesehen.Add(ort)) b.Stapel.Add(ort);
                        }
                        break;
                    }
                }
            }
            catch (Exception ausnahme)
            {
                b.Fehler = ausnahme.GetType().Name + ": " + ausnahme.Message;
            }
            return b;
        }

        internal static readonly Regex HashMuster = new Regex("[0-9a-f]{32}", RegexOptions.Compiled);

        /** Die Exporte einer DLL, nach Adresse sortiert. */
        internal sealed class Exportliste
        {
            internal uint[] Rva;
            internal string[] Name;

            internal bool Naechster(uint rva, out string name, out uint abstand)
            {
                name = null;
                abstand = 0;
                var i = Array.BinarySearch(Rva, rva);
                if (i < 0) i = ~i - 1;
                if (i < 0) return false;
                name = Name[i];
                abstand = rva - Rva[i];
                return true;
            }
        }

        private static readonly Dictionary<string, Exportliste> _exporte = new Dictionary<string, Exportliste>();

        /**
         * Liest die Exporttabelle (PE/COFF, Datenverzeichnis 0). Bevorzugt
         * die Datei, die im Abbild steht; fehlt sie, die des laufenden Spiels.
         */
        internal static Exportliste Exporte(string pfad)
        {
            if (string.IsNullOrEmpty(pfad) || !File.Exists(pfad))
                pfad = Path.Combine(Application.dataPath, "Plugins", "x86_64", "lib_burst_generated.dll");
            if (_exporte.TryGetValue(pfad, out var fertig)) return fertig;
            Exportliste liste = null;
            try
            {
                using var strom = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new BinaryReader(strom);
                strom.Position = 0x3C;
                var pe = r.ReadInt32();
                strom.Position = pe + 6;
                int sektionen = r.ReadUInt16();
                strom.Position = pe + 20;
                int optGroesse = r.ReadUInt16();
                long opt = pe + 24;
                strom.Position = opt;
                var magic = r.ReadUInt16();
                strom.Position = opt + (magic == 0x20B ? 112 : 96);
                var expRva = r.ReadInt32();
                var expGroesse = r.ReadInt32();
                var tabelle = new (int Va, int Groesse, int Roh)[sektionen];
                for (var i = 0; i < sektionen; i++)
                {
                    strom.Position = opt + optGroesse + i * 40L + 8;
                    var vGroesse = r.ReadInt32();
                    var va = r.ReadInt32();
                    var rohGroesse = r.ReadInt32();
                    var roh = r.ReadInt32();
                    tabelle[i] = (va, Math.Max(vGroesse, rohGroesse), roh);
                }
                long Datei(int rva)
                {
                    foreach (var s in tabelle)
                        if (rva >= s.Va && rva < s.Va + s.Groesse) return rva - s.Va + s.Roh;
                    throw new InvalidDataException("RVA 0x" + rva.ToString("X") + " in keiner Sektion");
                }
                strom.Position = Datei(expRva);
                var block = r.ReadBytes(expGroesse);
                int I32(int o) => BitConverter.ToInt32(block, o);
                int ImBlock(int rva) => rva - expRva;
                var nFunktionen = I32(20);
                var nNamen = I32(24);
                var funktionen = ImBlock(I32(28));
                var namen = ImBlock(I32(32));
                var ordinale = ImBlock(I32(36));
                var paare = new List<(uint, string)>(nNamen);
                for (var i = 0; i < nNamen; i++)
                {
                    var nameOff = ImBlock(I32(namen + i * 4));
                    var ende = Array.IndexOf(block, (byte)0, nameOff);
                    var name = Encoding.ASCII.GetString(block, nameOff, ende - nameOff);
                    int ordinal = BitConverter.ToUInt16(block, ordinale + i * 2);
                    if (ordinal >= nFunktionen) continue;
                    paare.Add(((uint)I32(funktionen + ordinal * 4), name));
                }
                paare.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                liste = new Exportliste
                {
                    Rva = paare.Select(p => p.Item1).ToArray(),
                    Name = paare.Select(p => p.Item2).ToArray(),
                };
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzabbild: Exporte von " + Path.GetFileName(pfad)
                    + " nicht lesbar: " + ausnahme.Message);
            }
            _exporte[pfad] = liste;
            return liste;
        }
    }
}
