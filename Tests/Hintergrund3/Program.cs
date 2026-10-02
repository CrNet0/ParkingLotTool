using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Game.Tools;
using ParkingLotTool.Geometry;

// Nur unsere Zulassung wird ausgefuehrt. Vanilla wird als IL gelesen:
// keine ECS-/Burst-Ausfuehrung, kein kopierter Resolver, kein Spieltest.
var managed = Environment.GetEnvironmentVariable("CSII_MANAGEDPATH")
    ?? Environment.GetEnvironmentVariable("CSII_MANAGEDPATH", EnvironmentVariableTarget.User);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(managed ?? "", name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
return Messen();

static int Messen()
{
    if (Environment.GetEnvironmentVariable("PLT_SCHREIBERINVENTAR") == "1")
    {
        var asm = typeof(CreationDefinition).Assembly;
        var schreiber = new SortedSet<string>();
        int methoden = 0, schreibstellen = 0;
        foreach (var type in asm.GetTypes())
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance)))
            {
                if (method.GetMethodBody() == null) continue;
                methoden++;
                foreach (var call in Aufrufe(method).OfType<MethodInfo>())
                {
                    bool komponente = call.IsGenericMethod
                        && call.GetGenericArguments().Contains(typeof(CreationDefinition))
                        && (call.Name == "AddComponent" || call.Name == "SetComponent"
                            || call.Name == "AddComponentData" || call.Name == "SetComponentData");
                    bool direkterChunk = call.Name == "set_Item" && call.DeclaringType.IsGenericType
                        && call.DeclaringType.GetGenericArguments().Contains(typeof(CreationDefinition));
                    if (!komponente && !direkterChunk) continue;
                    schreibstellen++;
                    schreiber.Add(type.FullName + "." + method.Name);
                }
            }
        foreach (var name in schreiber) Console.WriteLine(name);
        Console.WriteLine($"{methoden} IL-Methoden; {schreibstellen} CreationDefinition-Schreibaufrufe; "
            + $"{schreiber.Count} schreibende Methoden.");
        return 0;
    }
    int fehler = 0, pruefungen = 0;
    void Pruefe(bool stimmt, string text)
    {
        pruefungen++;
        if (!stimmt) fehler++;
        Console.WriteLine((stimmt ? "OK: " : "FEHLER: ") + text);
    }
    // Unabhaengige Wahrheitstabelle: gueltige 0/0-Situation MUSS starten.
    foreach (bool bereit in new[] { false, true })
        foreach (int definitionen in new[] { 0, 1, 37 })
            foreach (int temps in new[] { 0, 1, 514 })
            {
                bool soll = bereit && definitionen + temps == 0;
                Pruefe(ExklusivesBaubild.DarfAnlegen(definitionen, temps, bereit) == soll,
                    $"Zulassung bereit={bereit}, Definitionen={definitionen}, Temps={temps}: {soll}.");
            }
    Pruefe(ExklusivesBaubild.Wartegrund(37, 514, true).Contains("37")
        && ExklusivesBaubild.Wartegrund(37, 514, true).Contains("514"),
        "Warten nennt beide gemessenen Mengen.");
    bool negativAbgewiesen = false;
    try { ExklusivesBaubild.DarfAnlegen(-1, 0, true); }
    catch (ArgumentOutOfRangeException) { negativAbgewiesen = true; }
    Pruefe(negativAbgewiesen, "Negative Messung wird abgewiesen.");

    if (Environment.GetEnvironmentVariable("PLT_NUR_ZULASSUNG") == "1")
        return Ergebnis();
    var assembly = typeof(CreationDefinition).Assembly;
    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var job = assembly.GetType("Game.Tools.FindOwnersSystem+SetSubEntityOwnerJob", true);
    var execute = job.GetMethods(flags).Single(m => m.Name == "Execute");
    var calls = Aufrufe(execute).ToArray();
    var nativeTypes = calls.OfType<MethodInfo>()
        .Where(m => m.Name == "GetNativeArray" && m.IsGenericMethod)
        .SelectMany(m => m.GetGenericArguments()).Select(t => t.FullName).ToArray();
    Pruefe(nativeTypes.Contains("Game.Objects.Transform"), "Echter Besitzerresolver liest Transform.");
    Pruefe(nativeTypes.Contains("Game.Net.Curve"), "Echter Besitzerresolver liest Curve.");
    Pruefe(!nativeTypes.Contains("Game.Areas.Area") && !nativeTypes.Contains("Game.Areas.Node"),
        "Echter Besitzerresolver liest keine Area-/AreaNode-Lage.");
    var lookupTypes = job.GetFields(flags).Select(f => f.FieldType.ToString()).ToArray();
    Pruefe(!lookupTypes.Any(t => t.Contains("Game.Areas.Node") || t.Contains("Game.Areas.Area")),
        "Kein weiterer Area-Lookup am echten Resolverjob.");
    var ownerFields = typeof(OwnerDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
    Pruefe(ownerFields.Length == 3
        && ownerFields.Single(f => f.Name == "m_Prefab").FieldType == typeof(Unity.Entities.Entity),
        "OwnerDefinition: genau Prefab/Lage/Rotation, kein Definitionsergebnis-Verweis.");

    var areaJob = assembly.GetType("Game.Tools.GenerateAreasSystem+CreateAreasJob", true);
    var create = areaJob.GetMethod("CreateAreas", flags);
    var areaCalls = Aufrufe(create).ToArray();
    Pruefe(areaCalls.Any(m => m.Name == "CreateEntity"
        && m.DeclaringType.FullName == "Unity.Entities.EntityCommandBuffer"),
        "GenerateAreas erzeugt die Ergebnis-Entity im eigenen CommandBuffer.");
    Pruefe(!areaCalls.Any(m => m.Name == "Instantiate"),
        "GenerateAreas instanziiert die Definitions-Entity nicht als Besitzer.");

    // Owner-in-Definition nimmt denselben Kurszweig wie Vanilla-Unternetze.
    // Das Verhalten wird hier nicht simuliert: nur die reale IL-Schnittstelle.
    var courseJob = assembly.GetType("Game.Tools.CourseSplitSystem+SplitCoursesJob", false);
    if (courseJob == null)
        courseJob = assembly.GetTypes().Single(t => t.DeclaringType?.FullName == "Game.Tools.CourseSplitSystem"
            && t.GetMethods(flags).Any(m => m.Name == "CalculateElevation"));
    var checks = courseJob.GetMethods(flags).Where(m => m.Name == "CalculateElevation").ToArray();
    Pruefe(checks.Length == 2, "Vanilla-Elevationsweg: beide CalculateElevation-Ueberladungen vorhanden.");
    Console.WriteLine("PLT-Hintergrund: Besitzerbefund ist IL-Inspektion, keine materialisierte Transaktion.");
    return Ergebnis();

    int Ergebnis()
    {
        Console.WriteLine($"PLT-Hintergrund: Runde 3, {pruefungen} Pruefungen, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}

static IEnumerable<MethodBase> Aufrufe(MethodBase method)
{
    var bytes = method.GetMethodBody()?.GetILAsByteArray()
        ?? throw new InvalidOperationException("IL fehlt: " + method.Name);
    var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
        .ToDictionary(c => unchecked((ushort)c.Value));
    for (int i = 0; i < bytes.Length;)
    {
        ushort value = bytes[i++];
        if (value == 0xfe) value = (ushort)(0xfe00 | bytes[i++]);
        var code = codes[value];
        int size = code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
            _ => 4,
        };
        if (code.OperandType == OperandType.InlineMethod)
        {
            MethodBase resolved;
            try
            {
                resolved = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i),
                    method.DeclaringType.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("IL-Aufruf konnte nicht aufgeloest werden: "
                    + method.DeclaringType.FullName + "." + method.Name, ex);
            }
            yield return resolved;
        }
        i += size;
    }
}
