using System;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;

namespace Unity.Entities
{
    public readonly record struct Entity(int Index) { public static Entity Null => default; }
    public sealed class EntityManager
    {
        public readonly Dictionary<Entity, Dictionary<Type, object>> Data = new();
        public bool Exists(Entity e) => Data.ContainsKey(e);
        public void Set<T>(Entity e, T value)
        {
            if (!Data.ContainsKey(e)) Data[e] = new();
            Data[e][typeof(T)] = value;
        }
        public bool HasComponent<T>(Entity e) => Exists(e) && Data[e].ContainsKey(typeof(T));
        public bool HasBuffer<T>(Entity e) => HasComponent<List<T>>(e);
        public T GetComponentData<T>(Entity e) => (T)Data[e][typeof(T)];
        public List<T> GetBuffer<T>(Entity e, bool _) => GetComponentData<List<T>>(e);
    }
}
namespace Game.Common { public struct Deleted { } }
namespace Game.Tools { public struct Temp { } }
namespace Game.Net
{
    public struct Edge { }
    public struct SubNet { public Entity m_SubNet; }
    public struct SubLane { public Entity m_SubLane; }
    public struct CarLane { }
}
namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private EntityManager EntityManager = new();
        private PrefabSystem _prefabSystem = new();

        internal static void PruefeFahrwegeMigration(Action<bool, string> pruefe)
        {
            var t = new ParkingLotToolSystem();
            var traeger = new Entity(1); var kante = new Entity(2); var netz = new Entity(3);
            var lane = new Entity(4); var lanePrefab = new Entity(5); var pfad = new Entity(6);
            t.EntityManager.Set(traeger, new List<Game.Net.SubNet> { new() { m_SubNet = kante } });
            t.EntityManager.Set(kante, new Game.Net.Edge());
            t.EntityManager.Set(kante, new PrefabRef { m_Prefab = netz });
            t._prefabSystem.Names[netz] = "Invisible Road Path - 2xTwoway";
            pruefe(t.BrauchtFahrwegeNeubau(traeger), "Migration erkennt Vanilla-Innenweg auch vor Lane-Erzeugung");
            pruefe(!t.HatGebauteFahrspuren(traeger), "Neubau ohne wirkliche Autospuren ist kein Erfolg");
            t._prefabSystem.Names[netz] = "PLT Invisible Road Path - 2xTwoway";
            t.EntityManager.Set(kante, new List<Game.Net.SubLane> { new() { m_SubLane = lane } });
            t.EntityManager.Set(lane, new Game.Net.CarLane());
            pruefe(t.HatGebauteFahrspuren(traeger), "wirklich gebaute Autospur als positiver Nachweis");
            t.EntityManager.Set(lane, new PrefabRef { m_Prefab = lanePrefab });
            t.EntityManager.Set(lanePrefab, new NetLaneData { m_PathfindPrefab = pfad });
            t.EntityManager.Set(pfad, new PathfindCarData { m_DrivingCost = ParkingLotFahrregeln.Fahrkosten.ToPathfindCosts() });
            t._prefabSystem.Names[lanePrefab] = "Invisible Car Oneway Lane 3";
            t._prefabSystem.Names[pfad] = "Invisible Path Pathfind";
            pruefe(t.BrauchtFahrwegeNeubau(traeger), "gleiche Kosten an fremder Lane ersetzen die Isolation nicht");
            t._prefabSystem.Names[lanePrefab] = t._prefabSystem.Names[netz] + " :: Lane";
            t._prefabSystem.Names[pfad] = t._prefabSystem.Names[netz] + " :: Pathfind";
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "korrekt umgestellter Parkplatz braucht keinen weiteren Neubau");
            t._prefabSystem.Names[lanePrefab] = "PLT Nachbarweg :: Lane";
            t._prefabSystem.Names[pfad] = "PLT Nachbarweg :: Pathfind";
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "korrekte Verbindungslane aus benachbartem PLT-Prefab erhalten");
            t.EntityManager.Set(pfad, new PathfindCarData { m_DrivingCost = new PathfindCostInfo(0, .01f, .01f, .01f).ToPathfindCosts() });
            pruefe(t.BrauchtFahrwegeNeubau(traeger), "gespeicherte alte Kosten werden erkannt");
            t._prefabSystem.Names[netz] = "PLT Zoningstrasse (Alley)";
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "erhaltene Zoningstrasse fuehrt nicht zum endlosen Neubau");
            t._prefabSystem.Names[netz] = "Alley";
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "fremde Stadtstrasse unangetastet");
            t._prefabSystem.Names[netz] = "Invisible Road Path - 2xTwoway";
            t.EntityManager.Set(kante, new Game.Common.Deleted());
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "geloeschte Kante ignoriert");
            t.EntityManager.Data[kante].Remove(typeof(Game.Common.Deleted));
            t.EntityManager.Set(kante, new Game.Tools.Temp());
            pruefe(!t.BrauchtFahrwegeNeubau(traeger), "temporaere Kante ignoriert");
            pruefe(!t.BrauchtFahrwegeNeubau(new Entity(999)), "fehlender Traeger ohne Effekt");
        }
    }
}
