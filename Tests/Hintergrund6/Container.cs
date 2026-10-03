using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

// Fuehrt den echten Bauzettelschreiber/-leser auf Datencontainern aus.
// Keine ECS-/Vanilla-Materialisierung. Gepufferte Sichten werden nach jeder
// Strukturveraenderung ungueltig; IEnumerable auf DynamicBuffer wirft.
namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IQueryTypeParameter { }
    public interface IBufferElementData { }
    public sealed class InternalBufferCapacityAttribute : Attribute
    { public InternalBufferCapacityAttribute(int capacity) { } }
    public struct Entity : IEquatable<Entity>
    {
        public int Index, Version;
        public static Entity Null => default;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object other) => other is Entity e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(Index,Version);
        public static bool operator ==(Entity a,Entity b) => a.Equals(b);
        public static bool operator !=(Entity a,Entity b) => !a.Equals(b);
    }
    public struct DynamicBuffer<T> : IEnumerable<T>
    {
        private readonly List<T> _werte;
        private readonly EntityManager _em;
        private readonly int _stand;
        public DynamicBuffer(List<T> werte,EntityManager em) { _werte=werte; _em=em; _stand=em.Strukturstand; }
        private void Pruefe() { if (_stand != _em.Strukturstand) throw new InvalidOperationException("Veraltete Puffersicht: " + typeof(T).Name); }
        public int Length { get { Pruefe(); return _werte.Count; } }
        public T this[int index] { get { Pruefe(); return _werte[index]; } }
        public void Clear() { Pruefe(); _werte.Clear(); }
        public void Add(T wert) { Pruefe(); _werte.Add(wert); }
        public IEnumerator<T> GetEnumerator() => throw new NotImplementedException("Kein LINQ auf DynamicBuffer");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class EntityManager
    {
        private readonly Dictionary<(Entity,Type),object> _werte = new();
        private readonly HashSet<Entity> _lebt = new();
        public int Strukturstand { get; private set; }
        public void Anlegen(Entity e) => _lebt.Add(e);
        public bool Exists(Entity e) => _lebt.Contains(e);
        public bool HasComponent<T>(Entity e) => _werte.ContainsKey((e,typeof(T)));
        public bool HasBuffer<T>(Entity e) => _werte.ContainsKey((e,typeof(List<T>)));
        public T GetComponentData<T>(Entity e) => (T)_werte[(e,typeof(T))];
        public void AddComponentData<T>(Entity e,T v) { _werte.Add((e,typeof(T)),v); Strukturstand++; }
        public void SetComponentData<T>(Entity e,T v) => _werte[(e,typeof(T))]=v;
        public void RemoveComponent<T>(Entity e)
        { if (_werte.Remove((e,typeof(T))) | _werte.Remove((e,typeof(List<T>)))) Strukturstand++; }
        public DynamicBuffer<T> AddBuffer<T>(Entity e)
        { var liste=new List<T>(); _werte.Add((e,typeof(List<T>)),liste); Strukturstand++; return new(liste,this); }
        public DynamicBuffer<T> GetBuffer<T>(Entity e,bool readOnly=false) => new((List<T>)_werte[(e,typeof(List<T>))],this);
    }
}
namespace Colossal.Serialization.Entities
{
    public interface IWriter { void Write<T>(T value); }
    public interface IReader { void Read<T>(out T value); }
    public interface ISerializable
    {
        void Serialize<TWriter>(TWriter writer) where TWriter : IWriter;
        void Deserialize<TReader>(TReader reader) where TReader : IReader;
    }
}
namespace ParkingLotTool
{
    public static class ParkingLotTexte { public static string T(string de,string en) => de; }
    public static class Mod
    {
        public static readonly Log log = new();
        public sealed class Log
        {
            public string LetzterFehler;
            public void Info(string text) { }
            public void Error(Exception e,string text) { LetzterFehler=text+": "+e.Message; }
        }
    }
}
namespace ParkingLotTool.Tools
{
    public struct ParkingLotCarrierReference { public Unity.Entities.Entity Carrier; }
    public sealed partial class ParkingLotToolSystem
    {
        internal readonly Unity.Entities.EntityManager EntityManager = new();
        private const int MinPolygonPoints=3;
        private bool IsEditing => false;
        private string ZoningSeite => "Beides";
        internal bool Schreibe(Unity.Entities.Entity e,Bauzettelquelle q) => SchreibeBauzettel(e,q);
        private void WriteVegetation(Unity.Entities.Entity e) => AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(e),4,"{}");
        private void SchreibeVegetationAusProtokoll(Unity.Entities.Entity e,string json) => AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(e),4,json);
        internal sealed class Ausrichtzuweisung
        { internal float2 Anker,LinieA,LinieB; internal double Winkel; }
    }
}
