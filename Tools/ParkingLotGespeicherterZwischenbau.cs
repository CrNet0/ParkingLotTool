using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotNebenarbeitOffen : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(1);
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }
    // Vor dem Generator ist dessen Ergebnis-ID noch unbekannt. Die gesendete
    // Lot-Kontur identifiziert nach Laden auch die Luecke A -> Besitzerfund.
    public struct ParkingLotStufeAPrefab : IComponentData, ISerializable
    {
        public Entity Prefab;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(Prefab);
        public void Deserialize<TReader>(TReader r) where TReader : IReader => r.Read(out Prefab);
    }
    [InternalBufferCapacity(0)]
    public struct ParkingLotStufeAKnoten : IBufferElementData, ISerializable
    {
        public float3 Position;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => w.Write(Position);
        public void Deserialize<TReader>(TReader r) where TReader : IReader => r.Read(out Position);
    }
    [InternalBufferCapacity(0)]
    public struct ParkingLotRueckweganschluss : IBufferElementData, ISerializable
    {
        public int Kursindex;
        public Entity Knoten;
        public Entity Prefab, Besitzer;
        public float3 Position;
        public float Lage;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
        { w.Write(Kursindex); w.Write(Knoten); w.Write(Lage); w.Write(Prefab); w.Write(Besitzer); w.Write(Position); }
        public void Deserialize<TReader>(TReader r) where TReader : IReader
        { r.Read(out Kursindex); r.Read(out Knoten); r.Read(out Lage); r.Read(out Prefab); r.Read(out Besitzer); r.Read(out Position); }
    }
}
