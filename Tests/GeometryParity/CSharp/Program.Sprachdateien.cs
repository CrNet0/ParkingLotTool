using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ParkingLotTool.Geometry;

/**
 * Laedt `Lang/*.json` fuer den Testlauf - dieselben Dateien, die der Mod
 * neben seine DLL legt. Der Mod liest mit Newtonsoft, hier liest
 * System.Text.Json; beide fuellen nur `Sprachtexte.Sprachen`.
 */
internal static partial class Program
{
    private static string _sprachordner;

    private static string Sprachordner()
    {
        if (_sprachordner != null) return _sprachordner;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lang", "en-US.json")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("Lang/en-US.json nicht gefunden");
        return _sprachordner = Path.Combine(dir.FullName, "Lang");
    }

    private static void LadeSprachdateien()
    {
        if (Sprachtexte.Sprachen.Count > 0) return;
        foreach (var datei in Directory.GetFiles(Sprachordner(), "*.json"))
        {
            var texte = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(datei));
            Sprachtexte.Sprachen[Path.GetFileNameWithoutExtension(datei)] =
                new Dictionary<string, string>(texte, StringComparer.Ordinal);
        }
    }

    private static string De(string schluessel, params (string Name, object Wert)[] werte)
        => Sprachtexte.TextIn("de-DE", schluessel, werte);

    private static string En(string schluessel, params (string Name, object Wert)[] werte)
        => Sprachtexte.TextIn("en-US", schluessel, werte);
}
