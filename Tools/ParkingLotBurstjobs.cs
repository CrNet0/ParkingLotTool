using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * VOM BURST-HASH ZUM JOBNAMEN (2026-10-04).
     *
     * `lib_burst_generated.dll` exportiert jede Burst-Funktion nur unter einem
     * Hash. Ein Absturzabbild sagt deshalb "Job 2057be4e...", nicht
     * "AreaBatchSystem.BatchAllocationJob". Wie der Hash entsteht, rechnet
     * Unity nativ aus; nachbauen hiesse raten.
     *
     * Geraten wird hier nicht. Jeder Job-Erzeuger (`IJobExtensions.JobStruct`,
     * `JobChunkExtensions.JobChunkProducer` und so weiter) legt mit
     * `JobsUtility.CreateJobReflectionData` einen nativen Block an, in dem
     * Unity den Zeiger auf die Burst-Funktion ablegt. Der MESSLAUF ruft fuer
     * jeden Jobtyp das `Initialize()` seines Erzeugers, liest den Block und
     * sucht darin Zeiger, die genau auf einen Export zeigen. Der Name dieses
     * Exports ist der Hash.
     *
     * GEMESSEN 2026-10-04 im Hauptmenue: 90 von 1542 Jobs - genau die, die
     * dort schon gelaufen waren (Rendering, Prefabs). Unity traegt den
     * Burst-Zeiger erst beim ersten Lauf eines Jobs ein. Deshalb misst der
     * Lauf, solange der Schalter liegt, im Spiel alle 5 Minuten nach, liest
     * dann nur noch die fehlenden Bloecke und ergaenzt die Tabelle.
     *
     * Ein Werkzeug fuer uns, keine Funktion fuer Spieler: es laeuft nur, wenn
     * `Logs/PLT-BURSTJOBS.txt` existiert. Das Ergebnis
     * (`Logs/ParkingLotTool-burstjobs.txt`) wird als Ressource in den Mod
     * gelegt, damit jeder Absturzbericht Namen traegt. Die Tabelle gilt fuer
     * eine Spielversion; nach einem Spielupdate nennt der Bericht wieder nur
     * Hashes.
     */
    internal static class ParkingLotBurstjobs
    {
        private const string Schalter = "PLT-BURSTJOBS.txt";
        private const string Ergebnis = "ParkingLotTool-burstjobs.txt";
        private const string Ressource = "ParkingLotTool.burst-jobs.txt";
        private const float Abstand = 300f;

        private static string Logs => Path.Combine(Application.persistentDataPath, "Logs");

        private static Dictionary<string, string> _tabelle;
        private static string _stand = "not loaded";

        /** Woher die Namen stammen - steht unter jedem Abbildbericht. */
        internal static string Stand
        {
            get { Lade(); return _stand; }
        }

        internal static string Name(string hash)
        {
            Lade();
            return hash != null && _tabelle.TryGetValue(hash, out var name) ? name : null;
        }

        private static void Lade()
        {
            if (_tabelle != null) return;
            _tabelle = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                // Eine frische Messung auf diesem Rechner geht vor; sonst die
                // mitgelieferte Tabelle.
                var datei = Path.Combine(Logs, Ergebnis);
                string text = null, quelle = null;
                if (File.Exists(datei)) { text = File.ReadAllText(datei); quelle = "measured on this PC"; }
                else
                {
                    using var strom = typeof(ParkingLotBurstjobs).Assembly.GetManifestResourceStream(Ressource);
                    if (strom != null)
                    {
                        using var leser = new StreamReader(strom);
                        text = leser.ReadToEnd();
                        quelle = "shipped with the mod";
                    }
                }
                if (text == null) { _stand = "none - jobs are shown as hashes only"; return; }
                var version = "?";
                foreach (var zeile in text.Split('\n'))
                {
                    var z = zeile.TrimEnd('\r');
                    if (z.StartsWith("# game ", StringComparison.Ordinal)) { version = z.Substring(7); continue; }
                    if (z.Length == 0 || z[0] == '#') continue;
                    var teile = z.Split('\t');
                    if (teile.Length >= 2 && !_tabelle.ContainsKey(teile[0])) _tabelle[teile[0]] = teile[1];
                }
                _stand = _tabelle.Count + " jobs, " + quelle + ", game version " + version
                    + (version == Application.version ? "" : " (running " + Application.version
                        + " - names may be outdated)");
            }
            catch (Exception ausnahme)
            {
                _stand = "not readable: " + ausnahme.Message;
            }
        }

        private static float _naechsteMessung;

        /**
         * Jedes Bild aufrufen. Misst im ersten Bild und danach im Spiel alle
         * 5 Minuten - aber nur, solange der Schalter liegt.
         */
        internal static void Takt(bool imSpiel)
        {
            var jetzt = Time.realtimeSinceStartup;
            if (jetzt < _naechsteMessung) return;
            var erstesMal = _naechsteMessung == 0f;
            _naechsteMessung = jetzt + Abstand;
            if (!erstesMal && !imSpiel) return;
            if (!File.Exists(Path.Combine(Logs, Schalter))) return;
            try { Messe(); }
            catch (Exception ausnahme)
            {
                _naechsteMessung = float.MaxValue;
                Mod.log.Warn("PLT-Burstjobs: Messlauf abgebrochen: " + ausnahme);
            }
        }

        private sealed class Kandidat
        {
            internal Type Job;
            internal string Schnittstelle;
            internal IntPtr Block;
        }

        private static List<Kandidat> _offen;
        private static Dictionary<long, string> _adressen;
        private static readonly Dictionary<string, string> _gefunden = new Dictionary<string, string>(StringComparer.Ordinal);
        private static int _versucht, _gescheitert;

        private static void Messe()
        {
            var uhr = Stopwatch.StartNew();
            if (_offen == null && !Vorbereiten()) return;

            var neu = 0;
            for (var i = _offen.Count - 1; i >= 0; i--)
            {
                var k = _offen[i];
                var hashes = new List<string>();
                for (var off = 0; off < 256; off += 8)
                    if (_adressen.TryGetValue(Marshal.ReadInt64(k.Block, off), out var name))
                    {
                        var treffer = ParkingLotAbsturzabbild.HashMuster.Match(name);
                        if (treffer.Success && !hashes.Contains(treffer.Value)) hashes.Add(treffer.Value);
                    }
                if (hashes.Count == 0) continue;
                foreach (var h in hashes) _gefunden[h] = Jobname(k.Job) + "\t" + k.Schnittstelle;
                _offen.RemoveAt(i);
                neu++;
            }

            var text = new StringBuilder();
            text.AppendLine("# Parking Lot Tool - Burst job hash table (hash, job, interface)");
            text.AppendLine("# game " + Application.version);
            foreach (var paar in _gefunden.OrderBy(p => p.Key, StringComparer.Ordinal))
                text.AppendLine(paar.Key + "\t" + paar.Value);
            File.WriteAllText(Path.Combine(Logs, Ergebnis), text.ToString(), new UTF8Encoding(false));
            _tabelle = null;
            Mod.log.Info($"PLT-Burstjobs: Messung in {uhr.ElapsedMilliseconds} ms: {neu} Job(s) neu, "
                + $"{_gefunden.Count} Hashes in der Tabelle, {_offen.Count} von {_versucht} Jobs noch ohne "
                + $"Burst-Zeiger (noch nie gelaufen oder nicht mit Burst), {_gescheitert} nicht lesbar.");
        }

        /** Einmal je Sitzung: Exporte, Erzeuger, Jobtypen, Reflection-Bloecke. */
        private static bool Vorbereiten()
        {
            ProcessModule bibliothek = null;
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                if (string.Equals(m.ModuleName, "lib_burst_generated.dll", StringComparison.OrdinalIgnoreCase))
                { bibliothek = m; break; }
            if (bibliothek == null) { Mod.log.Warn("PLT-Burstjobs: lib_burst_generated.dll ist nicht geladen."); return false; }

            var basis = bibliothek.BaseAddress.ToInt64();
            var exporte = ParkingLotAbsturzabbild.Exporte(bibliothek.FileName);
            if (exporte == null) return false;
            _adressen = new Dictionary<long, string>();
            for (var i = 0; i < exporte.Rva.Length; i++)
            {
                var a = basis + exporte.Rva[i];
                if (!_adressen.ContainsKey(a)) _adressen[a] = exporte.Name[i];
            }

            // Fruehere Messungen behalten: was einmal gelaufen ist, laeuft in
            // dieser Sitzung vielleicht nicht wieder.
            var datei = Path.Combine(Logs, Ergebnis);
            if (File.Exists(datei))
                foreach (var zeile in File.ReadAllLines(datei))
                {
                    if (zeile.Length == 0 || zeile[0] == '#') continue;
                    var tab = zeile.IndexOf('\t');
                    if (tab > 0) _gefunden[zeile.Substring(0, tab)] = zeile.Substring(tab + 1);
                }

            var typen = AlleTypen();
            var erzeuger = new List<(Type Def, Type Schnittstelle, FieldInfo Feld)>();
            const BindingFlags Statisch = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var t in typen)
            {
                if (!t.IsValueType || !t.IsGenericTypeDefinition || t.GetGenericArguments().Length != 1) continue;
                if (t.GetMethod("Initialize", Statisch, null, Type.EmptyTypes, null) == null) continue;
                var feld = t.GetFields(Statisch).FirstOrDefault(f => f.FieldType.IsGenericType
                    && f.FieldType.Name.StartsWith("SharedStatic", StringComparison.Ordinal)
                    && f.FieldType.GetGenericArguments()[0] == typeof(IntPtr));
                if (feld == null) continue;
                foreach (var c in t.GetGenericArguments()[0].GetGenericParameterConstraints())
                    if (c.IsInterface) erzeuger.Add((t, c, feld));
            }

            var bekannt = new HashSet<string>(_gefunden.Values.Select(v => v.Split('\t')[0]), StringComparer.Ordinal);
            _offen = new List<Kandidat>();
            foreach (var job in typen)
            {
                if (!job.IsValueType || job.IsGenericTypeDefinition || job.ContainsGenericParameters || job.IsPrimitive) continue;
                foreach (var (def, schnittstelle, feld) in erzeuger)
                {
                    if (!schnittstelle.IsAssignableFrom(job)) continue;
                    _versucht++;
                    if (bekannt.Contains(Jobname(job))) continue;
                    try
                    {
                        var zu = def.MakeGenericType(job);
                        zu.GetMethod("Initialize", Statisch, null, Type.EmptyTypes, null).Invoke(null, null);
                        var block = LiesSharedStatic(zu.GetField(feld.Name, Statisch));
                        if (block == IntPtr.Zero) { _gescheitert++; continue; }
                        _offen.Add(new Kandidat { Job = job, Schnittstelle = schnittstelle.Name, Block = block });
                    }
                    catch { _gescheitert++; }
                }
            }
            return true;
        }

        /**
         * `SharedStatic<IntPtr>.Data` liefert `ref IntPtr`; per Reflection
         * kommt man daran nicht heran. Eine kleine dynamische Methode liest
         * den Wert: Adresse des statischen Feldes, `get_Data`, Wert laden.
         */
        private static IntPtr LiesSharedStatic(FieldInfo feld)
        {
            var data = feld.FieldType.GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetGetMethod(true);
            if (data == null) return IntPtr.Zero;
            var methode = new DynamicMethod("PLT_LiesSharedStatic", typeof(IntPtr), Type.EmptyTypes,
                typeof(ParkingLotBurstjobs).Module, true);
            var il = methode.GetILGenerator();
            il.Emit(OpCodes.Ldsflda, feld);
            il.Emit(OpCodes.Call, data);
            il.Emit(OpCodes.Ldind_I);
            il.Emit(OpCodes.Ret);
            return (IntPtr)methode.Invoke(null, null);
        }

        private static string Jobname(Type t) => (t.FullName ?? t.Name).Replace('+', '.');

        private static List<Type> AlleTypen()
        {
            var liste = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try { liste.AddRange(assembly.GetTypes()); }
                catch (ReflectionTypeLoadException teil) { liste.AddRange(teil.Types.Where(t => t != null)); }
                catch { }
            }
            return liste;
        }
    }
}
