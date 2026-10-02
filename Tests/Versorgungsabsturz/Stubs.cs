using System;
using System.Collections.Generic;
using System.Linq;

// Testdouble fuer die originale Diagnose: keine nachgebaute Messfunktion.
// Native Job-Ausfuehrung und Flush(true) werden hier nicht simuliert.
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Unity.Entities
{
    public readonly record struct Entity(int Index, int Version = 1)
    { public static readonly Entity Null = default; }
    public readonly record struct ComponentType(Type Type, bool Excluded)
    {
        public static ComponentType ReadOnly<T>() => new(typeof(T), false);
        public static ComponentType Exclude<T>() => new(typeof(T), true);
    }
    public sealed class FakeArray<T> : List<T>, IDisposable
    { public void Dispose() { } }
    public sealed class EntityQuery
    {
        public FakeManager Manager;
        public ComponentType[] Types;
        public FakeArray<Entity> ToEntityArray(Unity.Collections.Allocator _)
        {
            var result = new FakeArray<Entity>();
            result.AddRange(Manager.Data.Where(p => Types.All(t => p.Value.ContainsKey(t.Type) != t.Excluded)).Select(p => p.Key));
            return result;
        }
    }
    public sealed class FakeManager
    {
        public readonly Dictionary<Entity, Dictionary<Type, object>> Data = new();
        public void Add<T>(Entity e, T value)
        { if (!Data.ContainsKey(e)) Data[e] = new(); Data[e][typeof(T)] = value; }
        public bool Exists(Entity e) => Data.ContainsKey(e);
        public bool HasComponent<T>(Entity e) => Exists(e) && Data[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) => (T)Data[e][typeof(T)];
        public bool HasBuffer<T>(Entity e) => HasComponent<List<T>>(e);
        public FakeBuffer<T> GetBuffer<T>(Entity e, bool _ = false) => new(GetComponentData<List<T>>(e));
        public void RemoveComponent<T>(Entity e) => Data[e].Remove(typeof(T));
    }
    public sealed class FakeBuffer<T>(List<T> data) : IEnumerable<T>
    {
        public int Length => data.Count;
        public T this[int index] => data[index];
        public void RemoveAt(int index) => data.RemoveAt(index);
        public void Clear() => data.Clear();
        public void Add(T value) => data.Add(value);
        public IEnumerator<T> GetEnumerator() => data.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class FakeWorld
    {
        public readonly Dictionary<Type, object> Systems = new();
        public T GetExistingSystemManaged<T>() where T : class => Systems.GetValueOrDefault(typeof(T)) as T;
    }
}
namespace UnityEngine
{ public static class Time { public static int frameCount; } }
namespace UnityEngine.Scripting
{ public sealed class PreserveAttribute : Attribute { } }
namespace Game
{
    public enum SystemUpdatePhase { Modification1, Modification2 }
    public abstract class GameSystemBase
    {
        public Unity.Entities.FakeManager EntityManager = new();
        public Unity.Entities.FakeWorld World = new();
        protected abstract void OnUpdate();
        public void Tick() => OnUpdate();
        protected Unity.Entities.EntityQuery GetEntityQuery(params Unity.Entities.ComponentType[] types)
            => new() { Manager = EntityManager, Types = types };
    }
    public sealed class UpdateSystem
    {
        public readonly List<(SystemUpdatePhase Phase, bool VorNormal, Type System)> Registrierungen = new();
        public void UpdateBefore<T>(SystemUpdatePhase p) => Registrierungen.Add((p, true, typeof(T)));
    }
}
namespace Game.Common
{
    public struct Deleted { } public struct Created { } public struct Updated { }
    public struct Owner { public Unity.Entities.Entity m_Owner; }
}
namespace Game.Net
{
    public struct Edge { public Unity.Entities.Entity m_Start, m_End; }
    public struct Node { public string m_Position; }
    public struct Curve { public float m_Length; public FakeBezier m_Bezier; }
    public struct FakeBezier { public FakePoint a, d; }
    public struct FakePoint { public float y; }
    public struct ConnectedEdge { public Unity.Entities.Entity m_Edge; }
    public struct ConnectedNode { public Unity.Entities.Entity m_Node; public float m_CurvePosition; }
    public struct LocalConnect { }
    public struct Composition { public Unity.Entities.Entity m_Edge, m_StartNode, m_EndNode; }
    public struct SubLane { } public struct SubNet { public Unity.Entities.Entity m_SubNet; }
    public struct Orphan { public Unity.Entities.Entity m_Composition; }
}
namespace Game.Tools { public struct Temp { public Unity.Entities.Entity m_Original; } }
namespace Game.Prefabs
{
    public class PrefabBase { public string name; }
    public class PrefabSystem
    {
        public bool TryGetPrefab<T>(Unity.Entities.Entity e, out T asset) where T : PrefabBase
        { asset = null; return false; }
    }
    public struct PrefabRef { public Unity.Entities.Entity m_Prefab; }
    public struct NetCompositionData { }
    public struct NetCompositionMeshRef { public Unity.Entities.Entity m_Mesh; }
    public struct NetCompositionMeshData { }
}
namespace Game.Simulation
{
    public struct ElectricityNodeConnection { public Unity.Entities.Entity m_ElectricityNode; }
    public struct WaterPipeNodeConnection { public Unity.Entities.Entity m_WaterPipeNode; }
    public struct ConnectedFlowEdge { public Unity.Entities.Entity m_Edge; }
    public struct ElectricityFlowEdge { public Unity.Entities.Entity m_Start, m_End; }
    public struct WaterPipeEdge { public Unity.Entities.Entity m_Start, m_End; }
    public struct ElectricityFlowNode { } public struct WaterPipeNode { }
}
namespace ParkingLotTool.Tools
{
    public sealed class ParkingLotLeitungsabrissSystem { }
    public struct ParkingLotVersorgungsleitung { public Unity.Entities.Entity Lot, Carrier; }
    public static class Mod
    {
        public static readonly HashSet<string> Schalter = new();
        public static readonly FakeLog log = new();
        public static bool Aus(string s) => Schalter.Contains(s);
    }
    public sealed class FakeLog { public void Info(string _) { } }
    public static class ParkingLotLiveLog { public static void Zeile(string _) { } }
    public static class ParkingLotSchrittmarke
    {
        public static readonly List<string> Spur = new();
        public static void Setze(string _) { }
        public static void Versorgungsbild(string s) => Spur.Add(s);
    }
    public static class ParkingLotToolSystem
    { public static void AvDiagnoseMeldung(string s) => ParkingLotSchrittmarke.Spur.Add(s); }
}
