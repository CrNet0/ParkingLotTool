using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

// Befundmessung an der installierten Game.dll, kein uebernommener Spielcode.
// Keine ECS-Welt und keine Spiel-Entities werden erzeugt oder veraendert.
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
    var type = typeof(CreationDefinition).Assembly.GetType(
        "Game.Tools.GenerateEdgesSystem+NodeMapKey", throwOnError: true);
    var constructor = type.GetConstructor(new[] { typeof(CoursePos), typeof(bool), typeof(bool) });
    var equals = type.GetMethod("Equals", new[] { type });
    var hash = type.GetMethod("GetHashCode", Type.EmptyTypes);
    if (constructor == null || equals == null || hash == null)
        throw new InvalidOperationException("Spiel-Schnittstelle geaendert: Messung aktualisieren.");

    object Key(Entity entity, float3 position, bool permanent)
        => constructor.Invoke(new object[] { new CoursePos
            { m_Entity = entity, m_Position = position }, permanent, false });
    bool Gleich(object a, object b) => (bool)equals.Invoke(a, new[] { b });
    var knoten = new Entity { Index = 17, Version = 2 };
    var lage = new float3(100, 25, 200);
    var freiTemp = Key(Entity.Null, lage, false);
    var freiPermanent = Key(Entity.Null, lage, true);
    var anschlussTemp = Key(knoten, lage, false);
    var anschlussPermanent = Key(knoten, lage, true);
    var versetztPermanent = Key(knoten, lage + new float3(1, 2, 3), true);
    var andererKnoten = Key(new Entity { Index = 18, Version = 2 }, lage, true);
    var kollidiert = Gleich(anschlussTemp, anschlussPermanent);
    var fehler = 0;
    void Pruefe(bool stimmt, string text)
    {
        Console.WriteLine((stimmt ? "OK: " : "FEHLER: ") + text);
        if (!stimmt) fehler++;
    }
    Pruefe(!Gleich(freiTemp, freiPermanent), "Freie Lage trennt Temp/Permanent.");
    Pruefe(kollidiert, "Benannter Knoten vereinigt Temp/Permanent (Isolationsgrenze).");
    Pruefe(Gleich(anschlussTemp, versetztPermanent), "Benannter Knoten ignoriert auch die Lage.");
    Pruefe(!Gleich(anschlussTemp, andererKnoten), "Verschiedene Anschlussknoten bleiben getrennt.");
    Pruefe(hash.Invoke(anschlussTemp, null).Equals(hash.Invoke(anschlussPermanent, null)),
        "Temp/Permanent am selben Anschluss haben denselben Hash.");

    // Auch die vorhergehende Knotensammlung messen: ihr Schluessel besitzt
    // ueberhaupt kein Permanent-Feld. CollectUpdatesJob vereinigt bei gleichen
    // Schluesseln/ueberlappenden Kursbereichen die CreationFlags per ODER.
    var assembly = typeof(CreationDefinition).Assembly;
    var updateType = assembly.GetType("Game.Tools.GenerateNodesSystem+UpdateData", true);
    var nodeKeyType = assembly.GetType("Game.Tools.GenerateNodesSystem+NodeKey", true);
    var nodeConstructor = nodeKeyType.GetConstructor(new[] { updateType });
    var nodeEquals = nodeKeyType.GetMethod("Equals", new[] { nodeKeyType });
    object Update(Entity original, Entity owner, bool permanent)
    {
        var update = Activator.CreateInstance(updateType);
        updateType.GetField("m_Original").SetValue(update, original);
        updateType.GetField("m_Owner").SetValue(update, owner);
        updateType.GetField("m_Position").SetValue(update, lage);
        updateType.GetField("m_CreationFlags").SetValue(update,
            permanent ? CreationFlags.Permanent : default(CreationFlags));
        return nodeConstructor.Invoke(new[] { update });
    }
    bool KnotenGleich(object a, object b) => (bool)nodeEquals.Invoke(a, new[] { b });
    var eigenerOwner = new Entity { Index = 100, Version = 1 };
    var eigenerFreierKnoten = Update(Entity.Null, eigenerOwner, true);
    var fremderFreierKnoten = Update(Entity.Null, Entity.Null, false);
    var freieKollision = KnotenGleich(eigenerFreierKnoten, fremderFreierKnoten);
    Pruefe(freieKollision, "GenerateNodes: freie Temp/Permanent-Knoten kollidieren trotz verschiedener Besitzer.");
    Pruefe(KnotenGleich(Update(knoten, eigenerOwner, true), Update(knoten, Entity.Null, false)),
        "GenerateNodes: benannte Temp/Permanent-Knoten kollidieren trotz verschiedener Besitzer.");
    Pruefe(!KnotenGleich(Update(knoten, eigenerOwner, true),
        Update(new Entity { Index = 18, Version = 2 }, Entity.Null, false)),
        "GenerateNodes: verschiedene Originale trennen sich.");
    Console.WriteLine($"PLT-Hintergrund: Befundmessung 8 Pruefungen, {fehler} Messfehler; "
        + $"benannter Anschluss kollidiert={(kollidiert ? 1 : 0)}. "
        + $"Knotensammlung an freier Lage kollidiert={(freieKollision ? 1 : 0)}. "
        + "Dies ist KEIN bestandener Nachweis einer isolierten Bau-Transaktion.");
    return fehler == 0 ? 0 : 1;
}
