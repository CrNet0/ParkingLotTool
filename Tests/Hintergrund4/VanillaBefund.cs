using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

// Eigener IL-Leser; der Spielcode wird weder kopiert noch ausgefuehrt.
internal static class VanillaBefund
{
    internal static void Messen(Action<bool,string> pruefe)
    {
        var managed = Environment.GetEnvironmentVariable("CSII_MANAGEDPATH")
            ?? Environment.GetEnvironmentVariable("CSII_MANAGEDPATH",EnvironmentVariableTarget.User);
        AssemblyLoadContext.Default.Resolving += (_,name) => {
            var pfad = Path.Combine(managed ?? "",name.Name + ".dll");
            return File.Exists(pfad) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(pfad) : null;
        };
        var unity = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"Unity.Entities.dll"));
        var welt = unity.GetType("Unity.Entities.World",true);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var intern = welt.GetMethods(flags).Single(m => m.Name == "CreateSystemInternal");
        var calls = Aufrufe(intern).ToArray();
        pruefe(calls.Any(m => m.Name == "AddSystem_Add_Internal") && calls.Any(m => m.Name == "AddSystem_OnCreate_Internal"),"World erzeugt und initialisiert zweite Instanz");
        pruefe(!calls.Any(m => m.Name == "GetExistingSystemInternal"),"CreateSystemInternal lehnt vorhandenen Systemtyp nicht ab");
        var add = welt.GetMethods(flags).Single(m => m.Name == "AddSystemManaged" && m.IsGenericMethod);
        pruefe(Aufrufe(add).Any(m => m.Name == "GetExistingSystemInternal"),"AddSystemManaged besitzt die konkurrierende Typpruefung");
        var lookup = welt.GetMethods(flags).Single(m => m.Name == "AddTypeLookupInternal");
        var lookupCalls = Aufrufe(lookup).ToArray();
        pruefe(Array.FindIndex(lookupCalls,m => m.Name == "ContainsKey") < Array.FindIndex(lookupCalls,m => m.Name == "Add")
            && lookupCalls.Any(m => m.Name == "ContainsKey"),"Typ-Lookup prueft vorhandenen Eintrag vor Add; Quellenpruefung bestaetigt den negativen Zweig");
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"Game.dll"));
        var owner = game.GetType("Game.Common.Owner",true);
        var job = game.GetType("Game.Tools.GenerateObjectsSystem+CreateObjectsJob",true);
        var create = job.GetMethods(flags).Single(m => m.Name == "CreateObject");
        var objectCalls = Aufrufe(create).OfType<MethodInfo>().ToArray();
        pruefe(objectCalls.Any(m => m.Name == "AddComponent" && m.IsGenericMethod && m.GetGenericArguments().Contains(owner)),"Echter Objektgenerator besitzt AddComponent<Owner>");
        pruefe(Aufrufe(create).OfType<ConstructorInfo>().Any(m => m.DeclaringType == owner
            && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.FullName == "Unity.Entities.Entity"),"Owner-Konstruktor nimmt direkten Entity-Verweis");
        foreach (var name in new[] {"SubNetReferencesSystem","SubObjectReferencesSystem","SubAreaReferencesSystem"})
        {
            var system = game.GetTypes().Single(t => t.Name == name);
            var jobs = system.GetNestedTypes(BindingFlags.NonPublic).Where(t => t.Name.StartsWith("Update"));
            var jobCalls = jobs.SelectMany(t => t.GetMethods(flags).Where(m => m.Name == "Execute"))
                .SelectMany(Aufrufe).ToArray();
            pruefe(jobCalls.Any(m => m.Name == "TryAddUniqueValue"),system.FullName + ": Vanilla fuellt Besitzerpuffer mit TryAddUniqueValue");
            Console.WriteLine("IL-Beleg: " + system.FullName);
        }
        var search = game.GetType("Game.Areas.SearchSystem",true);
        pruefe(search.GetMethods(flags).Any(m => m.Name == "GetSearchTree" && m.GetParameters().Length == 3),"Area-Suche stellt synchronisierbaren Baum samt Dreieckszaehler bereit");
        Console.WriteLine("Vanilla-Befund: IL-Schnittstellen und Quellenbelege; keine ECS-Materialisierung.");
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

}
