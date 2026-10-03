using Unity.Entities;

// Nur die Datenformen der ECS-API. Die Messung selbst ist Produktionscode.
namespace Game.Net
{
    public struct Upgraded : IComponentData { }
    public struct CarLane : IComponentData { }
    public struct PedestrianLane : IComponentData { }
    public struct Lane : IComponentData { }
    public struct EdgeLane : IComponentData { }
    public struct SlaveLane : IComponentData { public ushort m_MasterIndex; }
    public struct NodeGeometry : IComponentData { }
    public struct EdgeGeometry : IComponentData { }
    public struct StartNodeGeometry : IComponentData { }
    public struct EndNodeGeometry : IComponentData { }
}
namespace Game.Objects
{
    public struct Attached : IComponentData { public Entity m_Parent, m_OldParent; public float m_CurvePosition; }
    public struct Attachment : IComponentData { public Entity m_Attached; }
    public struct Placeholder : IComponentData { }
    public struct NetObject : IComponentData { }
    public struct Transform : IComponentData { }
    public struct SubObject : IBufferElementData { public Entity m_SubObject; }
}
namespace Game.Buildings
{
    public struct Building : IComponentData { public Entity m_RoadEdge; }
    public struct ConnectedBuilding : IBufferElementData { public Entity m_Building; }
}
namespace Game.Simulation
{
    public struct ElectricityBuildingConnection : IComponentData { public Entity m_ProducerEdge, m_ConsumerEdge, m_ChargeEdge, m_DischargeEdge; }
    public struct WaterPipeBuildingConnection : IComponentData { public Entity m_ProducerEdge, m_ConsumerEdge; }
}
namespace Game.Prefabs
{
    public struct NetObjectData : IComponentData { public CompositionFlags m_CompositionFlags; }
    public struct PlaceableObjectData : IComponentData { }
    public struct TransportStopData : IComponentData { }
    public struct RouteConnectionData : IComponentData { }
    public struct NetData : IComponentData { }
    public struct NetGeometryData : IComponentData { }
    public struct RoadData : IComponentData { }
    public struct LocalConnectData : IComponentData { }
    public struct ElectricityConnectionData : IComponentData { }
    public struct WaterPipeConnectionData : IComponentData { }
    public struct BuildingData : IComponentData { }
    public struct NetPieceData : IComponentData { }
    public struct MeshData : IComponentData { }
    public struct NetLaneData : IComponentData { }
    public struct CarLaneData : IComponentData { }
    public struct UtilityLaneData : IComponentData { }
    public struct SecondaryLaneData : IComponentData { }
    public enum NetPieceFlags { HasMesh = 1 }
    public enum NetSectionFlags { Hidden = 1 }
    public struct NetCompositionPiece : IBufferElementData
    { public Entity m_Piece; public NetPieceFlags m_PieceFlags; public NetSectionFlags m_SectionFlags; }
    public struct NetCompositionLane : IBufferElementData { public Entity m_Lane; }
    public struct NetCompositionCrosswalk : IBufferElementData { public Entity m_Lane; }
    public struct NetGeometrySection : IBufferElementData { public Entity m_Section; }
    public struct NetSubSection : IBufferElementData { public Entity m_SubSection; }
    public struct NetSectionPiece : IBufferElementData { public Entity m_Piece; }
    public struct NetPieceLane : IBufferElementData { public Entity m_Lane; }
    public struct DefaultNetLane : IBufferElementData { public Entity m_Lane; }
    public struct SecondaryNetLane : IBufferElementData { public Entity m_Lane; }
    public struct AuxiliaryNetLane : IBufferElementData { public Entity m_Prefab; }
    public struct LodMesh : IBufferElementData { public Entity m_LodMesh; }
    public struct MeshMaterial : IBufferElementData { }
    public struct NetGeometryComposition : IBufferElementData { public Entity m_Composition; }
}
namespace Game.Tools
{
    public struct OwnerDefinition : IComponentData { }
}
namespace Game.Routes
{
    public struct TransportStop : IComponentData { }
    public struct ConnectedRoute : IBufferElementData { public Entity m_Waypoint; }
    public struct Connected : IComponentData { public Entity m_Connected; }
    public struct RouteLane : IComponentData { public Entity m_StartLane, m_EndLane; }
    public struct AccessLane : IComponentData { public Entity m_Lane; }
}
namespace ParkingLotTool.Tools
{
    public struct ParkingLotPartRelation : IComponentData { public Entity Lot, Carrier; }
}
